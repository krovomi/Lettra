using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lettra.Application.Abstractions.Ai;

namespace Lettra.Ai.Privacy;

/// <summary>
/// Pseudonymisation d'un échange avec le moteur (ADR-0012, annexe F §10). Remplace noms, SIREN, SIRET, numéros de TVA,
/// IBAN, e-mails, téléphones et adresses par des jetons ([CLIENT_1], [IBAN_1]…) avant l'envoi, et rétablit les valeurs
/// au retour, côté serveur. Les montants, taux et dates restent lisibles : le raisonnement en a besoin.
/// La table de correspondance ne quitte jamais le serveur et vit le temps d'une conversation.
/// </summary>
public sealed partial class PseudonymizationSession
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private readonly Dictionary<string, string> _tokenToValue = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _keyToToken = new(StringComparer.Ordinal);
    private readonly Dictionary<IdentityKind, int> _counters = [];
    private readonly List<(string Pattern, string Token)> _known = [];
    private Regex? _knownRegex;

    public PseudonymizationSession(IEnumerable<KnownIdentity> knownIdentities)
    {
        ArgumentNullException.ThrowIfNull(knownIdentities);
        foreach (KnownIdentity identity in knownIdentities.Where(i => !string.IsNullOrWhiteSpace(i.Value)))
        {
            RegisterKnown(identity.Kind, identity.Value.Trim());
        }
    }

    /// <summary>Correspondances jeton → valeur réelle (jamais envoyées au moteur).</summary>
    public IReadOnlyDictionary<string, string> Tokens => _tokenToValue;

    public string Pseudonymize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        if (_knownRegex is not null)
        {
            text = _knownRegex.Replace(text, m => _known.First(k => Regex.IsMatch(m.Value, "^" + k.Pattern + "$", Options)).Token);
        }

        text = EmailPattern().Replace(text, m => TokenFor(IdentityKind.Email, m.Value));
        text = IbanPattern().Replace(text, m => TokenFor(IdentityKind.Iban, m.Value));
        text = VatPattern().Replace(text, m => TokenFor(IdentityKind.VatNumber, m.Value));
        text = SiretPattern().Replace(text, m => TokenFor(IdentityKind.Siret, m.Value));
        text = SirenPattern().Replace(text, m => TokenFor(IdentityKind.Siren, m.Value));
        return PhonePattern().Replace(text, m => TokenFor(IdentityKind.Phone, m.Value));
    }

    /// <summary>Pseudonymise toutes les valeurs texte d'un document JSON (les nombres restent intacts).</summary>
    public string PseudonymizeJson(string json)
    {
        JsonNode? node = JsonNode.Parse(json);
        return node is null ? json : Walk(node, Pseudonymize)!.ToJsonString(AiToolResult.JsonOptions);
    }

    /// <summary>Rétablit les valeurs réelles dans un texte produit par le moteur.</summary>
    public string Restore(string text) =>
        string.IsNullOrEmpty(text) ? text : TokenPattern().Replace(text, m => _tokenToValue.GetValueOrDefault(m.Value, m.Value));

    /// <summary>Rétablit les valeurs réelles dans l'entrée JSON d'un appel d'outil, avant son exécution côté serveur.</summary>
    public JsonElement RestoreJson(JsonElement input)
    {
        JsonNode? node = JsonNode.Parse(input.GetRawText());
        return node is null ? input : JsonSerializer.SerializeToElement(Walk(node, Restore), AiToolResult.JsonOptions);
    }

    /// <summary>Jetons présents dans le texte mais inconnus de la session (inventés par le moteur).</summary>
    public IEnumerable<string> UnknownTokens(string text) =>
        TokenPattern().Matches(text).Select(m => m.Value).Where(t => !_tokenToValue.ContainsKey(t)).Distinct(StringComparer.Ordinal);

    private static bool IsIdentifier(IdentityKind kind) =>
        kind is IdentityKind.Siren or IdentityKind.Siret or IdentityKind.Iban or IdentityKind.VatNumber or IdentityKind.Phone;

    private static string Compact(string value) =>
        new([.. value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);

    private static string Prefix(IdentityKind kind) => kind switch
    {
        IdentityKind.Company => "SOCIETE",
        IdentityKind.Client => "CLIENT",
        IdentityKind.Person => "PERSONNE",
        IdentityKind.Siren => "SIREN",
        IdentityKind.Siret => "SIRET",
        IdentityKind.VatNumber => "TVA",
        IdentityKind.Iban => "IBAN",
        IdentityKind.Email => "EMAIL",
        IdentityKind.Address => "ADRESSE",
        IdentityKind.Phone => "TEL",
        _ => "DONNEE",
    };

    private static JsonNode? Walk(JsonNode? node, Func<string, string> transform)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (string key in obj.Select(kv => kv.Key).ToList())
                {
                    obj[key] = Walk(obj[key]?.DeepClone(), transform);
                }

                return obj;
            case JsonArray array:
                for (int i = 0; i < array.Count; i++)
                {
                    array[i] = Walk(array[i]?.DeepClone(), transform);
                }

                return array;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                return JsonValue.Create(transform(value.GetValue<string>()));
            default:
                return node;
        }
    }

    private void RegisterKnown(IdentityKind kind, string value)
    {
        string token = TokenFor(kind, value);
        AddKnownPattern(value, token);

        // Variante sans forme juridique (« Dupont SARL » → « Dupont »), pour les mentions informelles.
        if (kind is IdentityKind.Client or IdentityKind.Company)
        {
            string core = LegalFormSuffix().Replace(value, string.Empty).Trim();
            if (core.Length >= 4 && !string.Equals(core, value, StringComparison.Ordinal))
            {
                AddKnownPattern(core, token);
            }
        }

        // Identifiants : reconnus aussi avec ou sans espaces.
        if (IsIdentifier(kind))
        {
            string compact = Compact(value);
            AddKnownPattern(string.Join(@"\s?", compact.Select(c => Regex.Escape(c.ToString()))), token, isRegex: true);
        }

        _knownRegex = new Regex(
            @"(?<![\p{L}\p{N}])(?:" + string.Join('|', _known.OrderByDescending(k => k.Pattern.Length).Select(k => k.Pattern)) + @")(?![\p{L}\p{N}])",
            Options);
    }

    private void AddKnownPattern(string value, string token, bool isRegex = false)
    {
        string pattern = isRegex ? value : string.Join(@"\s+", WhitespaceRun().Split(value.Trim()).Select(Regex.Escape));
        if (_known.TrueForAll(k => !string.Equals(k.Pattern, pattern, StringComparison.Ordinal)))
        {
            _known.Add((pattern, token));
        }
    }

    private string TokenFor(IdentityKind kind, string value)
    {
        string key = kind + ":" + (IsIdentifier(kind) ? Compact(value) : value.ToUpperInvariant());
        if (_keyToToken.TryGetValue(key, out string? existing))
        {
            return existing;
        }

        // Un identifiant déjà connu sous un autre type (un SIRET détecté comme SIREN) garde son jeton.
        string compactKey = Compact(value);
        foreach ((string knownKey, string knownToken) in _keyToToken)
        {
            int separator = knownKey.IndexOf(':', StringComparison.Ordinal);
            if (Enum.TryParse(knownKey[..separator], out IdentityKind knownKind) && IsIdentifier(knownKind)
                && string.Equals(knownKey[(separator + 1)..], compactKey, StringComparison.Ordinal))
            {
                return knownToken;
            }
        }

        int n = _counters[kind] = _counters.GetValueOrDefault(kind) + 1;
        string token = string.Create(CultureInfo.InvariantCulture, $"[{Prefix(kind)}_{n}]");
        _keyToToken[key] = token;
        _tokenToValue[token] = value;
        return token;
    }

    [GeneratedRegex(@"\[(?:SOCIETE|CLIENT|PERSONNE|SIREN|SIRET|TVA|IBAN|EMAIL|ADRESSE|TEL|DONNEE)_\d+\]", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRun();

    [GeneratedRegex(@"[\s,]+(SAS|SASU|SARL|EURL|SA|SCI|SNC|SCP|SELARL|GmbH|Ltd|LLC|Inc|AG|BV)\.?$", Options)]
    private static partial Regex LegalFormSuffix();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Z]{2}[0-9]{2}(?: ?[A-Z0-9]{4}){2,7}(?: ?[A-Z0-9]{1,4})?(?![A-Za-z0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex IbanPattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:FR ?[0-9A-Z]{2} ?[0-9]{3} ?[0-9]{3} ?[0-9]{3}|(?:AT|BE|BG|CY|CZ|DE|DK|EE|EL|ES|FI|HR|HU|IE|IT|LT|LU|LV|MT|NL|PL|PT|RO|SE|SI|SK)U?[0-9A-Z]{8,12})(?![A-Za-z0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex VatPattern();

    [GeneratedRegex(@"(?<![0-9])[0-9]{3} ?[0-9]{3} ?[0-9]{3} ?[0-9]{5}(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex SiretPattern();

    // Un nombre à 9 chiffres suivi de décimales ou d'une devise est un montant, pas un SIREN.
    [GeneratedRegex(@"(?<![0-9.,])[0-9]{3} ?[0-9]{3} ?[0-9]{3}(?![0-9]|[.,][0-9]| ?(?:€|EUR))", RegexOptions.CultureInvariant)]
    private static partial Regex SirenPattern();

    [GeneratedRegex(@"(?<![0-9])(?:\+33 ?[1-9]|0[1-9])(?:[ .-]?[0-9]{2}){4}(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}
