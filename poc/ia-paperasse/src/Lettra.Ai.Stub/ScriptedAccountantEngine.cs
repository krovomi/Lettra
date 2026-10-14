using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lettra.Ai.Abstractions;

namespace Lettra.Ai.Stub;

/// <summary>
/// Moteur simulé déterministe, utilisé quand aucune clé d'API n'est disponible (tests, CI, démonstration hors ligne).
/// Il se comporte comme un modèle discipliné : il appelle les outils dans un ordre plausible et ne rédige qu'à partir
/// de leurs résultats. Il prouve la plomberie (droits, pseudonymisation, outils, journal), pas la qualité d'un vrai modèle.
/// </summary>
public sealed partial class ScriptedAccountantEngine : IAiEngine
{
    public string Name => "stub:scripted";

    public Task<EngineTurn> CompleteAsync(EngineRequest request, CancellationToken ct = default)
    {
        var state = new ConversationState(request);
        var turn = state.Feature switch
        {
            nameof(AiFeature.ExplainCompliance) => ExplainCompliance(state),
            nameof(AiFeature.EInvoicingDiagnosis) => EInvoicingDiagnosis(state),
            nameof(AiFeature.DraftFromText) => DraftFromText(state),
            _ => RegulatoryQuestion(state),
        };
        return Task.FromResult(turn);
    }

    // ------------------------------------------------------------------ Point 3

    private static EngineTurn ExplainCompliance(ConversationState s)
    {
        if (s.InvoiceId is { } id && s.Result("valider_facture") is null)
            return s.Call("valider_facture", new { facture_id = id });
        if (s.Result("lire_reference") is null)
            return s.Call("lire_reference", new { skill = "comptable", chemin = "facturation/mentions-obligatoires.md" });

        var check = s.Result("valider_facture");
        if (check is null || check.Value.IsError)
            return Answer("**Faits** : je n'ai pas pu lire la facture ouverte (" + (check?.Content ?? "aucune facture ouverte") + ").\n\n**Limites** : ouvrez la facture concernée puis relancez la question.");

        using var doc = JsonDocument.Parse(check.Value.Content);
        var root = doc.RootElement;
        var sb = new StringBuilder();
        sb.Append("**Faits** : la facture ").Append(root.GetProperty("numero").GetString())
          .Append(" est « ").Append(root.GetProperty("statut").GetString()).Append(" » selon les règles de Lettra.\n\n");
        var errors = root.GetProperty("erreurs").EnumerateArray().ToList();
        if (errors.Count > 0)
        {
            sb.Append("**Analyse** : ces mentions manquent et bloquent la validation :\n");
            foreach (var e in errors)
            {
                sb.Append("- ").Append(e.GetProperty("label").GetString());
                if (e.TryGetProperty("base_legale", out var b)) sb.Append(" (").Append(b.GetString()).Append(')');
                sb.Append('\n');
            }
            if (errors.Any(e => e.GetProperty("id").GetString() == "siren_client") && errors.Any(e => e.GetProperty("id").GetString() == "categorie_operation"))
                sb.Append("\nLe SIREN du client et la catégorie d'opération sont deux obligations distinctes de la réforme 2026 : il faut renseigner les deux.\n");
        }
        else sb.Append("**Analyse** : aucune mention obligatoire ne manque.\n");
        foreach (var w in root.GetProperty("avertissements").EnumerateArray())
            sb.Append("\n**Risques** : ").Append(w.GetProperty("label").GetString()).Append('\n');

        var totals = root.GetProperty("totaux");
        sb.Append("\n**Faits chiffrés (calculés par Lettra)** : total HT ").Append(Eur(totals.GetProperty("total_ht")))
          .Append(", TVA ").Append(Eur(totals.GetProperty("total_tva")))
          .Append(", total TTC ").Append(Eur(totals.GetProperty("total_ttc"))).Append(".\n");
        sb.Append(errors.Count > 0
            ? "\n**Actions** : compléter les champs signalés dans la fiche client ou la facture, puis valider vous-même la facture.\n"
            : "\n**Actions** : la facture peut être validée par vous, depuis l'écran de la facture.\n");
        sb.Append("\n**Limites** : source comptable/references/facturation/mentions-obligatoires.md (Paperasse). À confirmer par l'expert-comptable du dossier.");
        return Answer(sb.ToString());
    }

    // ------------------------------------------------------------------ Point 4

    private static EngineTurn EInvoicingDiagnosis(ConversationState s)
    {
        if (s.Result("contexte_dossier") is null) return s.Call("contexte_dossier", new { });
        if (s.Result("lire_reference") is null)
            return s.Call("lire_reference", new { skill = "comptable", chemin = "facturation/reforme-2026.md" });

        using var company = JsonDocument.Parse(s.Result("contexte_dossier")!.Value.Content);
        var e = company.RootElement.TryGetProperty("einvoicing", out var ei) ? ei : default;
        bool Flag(string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
        string Str(string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
        var hasSiren = company.RootElement.TryGetProperty("siren", out var siren) && siren.GetString() is { Length: > 0 };

        var sb = new StringBuilder("**Diagnostic e-facturation du dossier**\n\n| Point | État |\n|---|---|\n");
        sb.Append("| Plateforme Agréée choisie | ").Append(Str("pa").Length > 0 ? "🟢 " + Str("pa_name") : "🔴 aucune").Append(" |\n");
        sb.Append("| Réception des factures électroniques (obligatoire au 1er septembre 2026) | ").Append(Flag("reception_ready") ? "🟢 prête" : "🔴 non prête").Append(" |\n");
        sb.Append("| Émission (selon la taille de l'entreprise, 2026 ou 2027) | ").Append(Flag("emission_ready") ? "🟢 prête" : "🟡 à préparer").Append(" |\n");
        sb.Append("| E-reporting | ").Append(Flag("ereporting_ready") ? "🟢 prêt" : "🟡 à préparer").Append(" |\n");
        sb.Append("| Identifiant de l'entreprise (SIREN) | ").Append(hasSiren ? "🟢 renseigné" : "🔴 manquant").Append(" |\n");
        sb.Append("\n**Actions** : choisir une Plateforme Agréée et la raccorder au dossier dans les paramètres.\n");
        sb.Append("\n**Limites** : source comptable/references/facturation/reforme-2026.md. Calendrier à vérifier sur impots.gouv.fr.");
        return Answer(sb.ToString());
    }

    // ------------------------------------------------------------------ Point 1

    private static EngineTurn DraftFromText(ConversationState s)
    {
        var m = QuoteRequest().Match(s.Question);
        if (!m.Success)
            return Answer("Je n'ai pas compris la demande de devis. Précisez le client, la quantité, la prestation et le prix unitaire HT, par exemple « Devis Dupont, 3 h de conseil à 120 € HT ».");

        var client = m.Groups["client"].Value.Trim();
        if (s.Result("rechercher_client") is not { } search) return s.Call("rechercher_client", new { nom = client });
        if (search.IsError) return Answer("Je ne peux pas chercher ce client : " + search.Content);

        using var found = JsonDocument.Parse(search.Content);
        var matches = found.RootElement.EnumerateArray().ToList();
        if (matches.Count != 1)
            return Answer(matches.Count == 0 ? $"Aucun client {client} dans ce dossier." : $"Plusieurs clients correspondent à {client} : précisez lequel.");

        if (s.Result("brouillon_devis") is not { } draft)
        {
            var quantity = decimal.Parse(m.Groups["q"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            var price = decimal.Parse(m.Groups["pu"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            var unit = m.Groups["unit"].Value.StartsWith('h') ? "heure(s)" : "jour(s)";
            return s.Call("brouillon_devis", new
            {
                client_id = matches[0].GetProperty("client_id").GetString(),
                objet = Capitalize(m.Groups["desc"].Value.Trim()),
                lignes = new[] { new { designation = $"{Capitalize(m.Groups["desc"].Value.Trim())} ({quantity.ToString(CultureInfo.InvariantCulture)} {unit})", quantite = quantity, prix_unitaire_ht = price } },
            });
        }
        if (draft.IsError) return Answer("Le brouillon n'a pas pu être créé : " + draft.Content);

        using var created = JsonDocument.Parse(draft.Content);
        var totals = created.RootElement.GetProperty("totaux");
        return Answer($"J'ai préparé un **brouillon** de devis pour {client} : total HT {Eur(totals.GetProperty("total_ht"))}, " +
                      $"TVA {Eur(totals.GetProperty("total_tva"))}, total TTC {Eur(totals.GetProperty("total_ttc"))} (montants calculés par Lettra).\n\n" +
                      "Relisez-le, puis validez-le vous-même : je ne peux ni le valider ni l'envoyer.");
    }

    // ------------------------------------------------------------------ Point 6

    private static readonly (string Pattern, string Reference)[] Topics =
    [
        ("e-reporting|ereporting", "facturation/e-reporting.md"),
        ("plateforme|PA |PDP", "facturation/plateformes-agreees.md"),
        ("num[ée]rotation|num[ée]ro de facture|trou", "facturation/numerotation-conservation.md"),
        ("DOM|Corse|Guadeloupe|Martinique|R[ée]union", "regional.md"),
        ("mention", "facturation/mentions-obligatoires.md"),
    ];

    private static EngineTurn RegulatoryQuestion(ConversationState s)
    {
        if (Calculation(s) is { } calc) return calc;

        var reference = Topics.FirstOrDefault(t => Regex.IsMatch(s.Question, t.Pattern, RegexOptions.IgnoreCase)).Reference ?? "tva.md";
        if (s.Result("lire_reference") is not { } read)
            return s.Call("lire_reference", new { skill = "comptable", chemin = reference });
        if (read.IsError) return Answer("Je n'ai pas trouvé de fiche sur ce sujet : " + read.Content);

        var excerpt = BestParagraph(read.Content, s.Question);
        return Answer("**Faits** : extrait de la fiche comptable/references/" + reference + " :\n\n> " + excerpt.Replace("\n", "\n> ") +
                      "\n\n**Limites** : réponse tirée du référentiel Paperasse, à vérifier sur les sites officiels. À confirmer par l'expert-comptable du dossier.");
    }

    private static EngineTurn? Calculation(ConversationState s)
    {
        var q = s.Question;
        (string Op, object Args)? request = null;
        if (Regex.IsMatch(q, "prorata", RegexOptions.IgnoreCase) && Numbers(q) is [var montant, var jours, ..])
            request = ("prorata", new Dictionary<string, string> { ["montant"] = montant, ["jours"] = jours, ["base"] = Numbers(q).ElementAtOrDefault(2) ?? "365" });
        else if (Regex.IsMatch(q, @"acomptes? de TVA|tva-acomptes", RegexOptions.IgnoreCase) && Numbers(q) is [var tva, ..])
            request = ("tva-acomptes-rs", new Dictionary<string, string> { ["tva-n-1"] = tva });
        else if (Regex.IsMatch(q, @"\bIS\b|imp[ôo]t sur les soci[ée]t[ée]s", RegexOptions.IgnoreCase) && Numbers(q) is [var resultat, ..])
            request = ("is", new Dictionary<string, string> { ["resultat-fiscal"] = resultat, ["taux-reduit"] = "15", ["taux-normal"] = "25" });
        if (request is null) return null;

        if (s.Result("calculer") is not { } result) return s.Call("calculer", new { operation = request.Value.Op, parametres = request.Value.Args });
        return Answer(result.IsError
            ? "Le calcul a été refusé : " + result.Content
            : "**Calcul fait par Lettra (calc.js porté en C#)** :\n\n```\n" + result.Content.TrimEnd() + "\n```\n\n**Limites** : vérifier les taux en vigueur. À confirmer par l'expert-comptable du dossier.");
    }

    // ------------------------------------------------------------------ Outils internes

    private static EngineTurn Answer(string text) => new([new TextPart(text)], EngineStopReason.EndTurn, new EngineUsage(0, 0, 0, 0));

    private static string Eur(JsonElement amount) => amount.GetDecimal().ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',') + " EUR";

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static List<string> Numbers(string text) =>
        NumberPattern().Matches(text).Select(m => m.Value.Replace(" ", "").Replace(" ", "")).ToList();

    private static string BestParagraph(string markdown, string question)
    {
        var words = Regex.Matches(question.ToLowerInvariant(), @"\p{L}{5,}").Select(m => m.Value).ToHashSet();
        var paragraphs = markdown.Split("\n\n").Select(p => p.Trim()).Where(p => p.Length > 40 && !p.StartsWith('#')).ToList();
        var best = paragraphs.OrderByDescending(p => words.Count(w => p.Contains(w, StringComparison.OrdinalIgnoreCase))).FirstOrDefault() ?? "";
        return best.Length > 700 ? best[..700] + "…" : best;
    }

    [GeneratedRegex(@"devis\s+(?<client>.+?)\s*,\s*(?<q>\d+(?:[.,]\d+)?)\s*(?<unit>h|heures?|jours?|j)\b\s*(?:de\s+|d')?(?<desc>.+?)\s+à\s+(?<pu>\d+(?:[.,]\d+)?)\s*(?:€|EUR|euros?)\s*HT", RegexOptions.IgnoreCase)]
    private static partial Regex QuoteRequest();

    [GeneratedRegex(@"\d[\d  ]*(?:[.,]\d+)?")]
    private static partial Regex NumberPattern();

    /// <summary>Lecture de la conversation : fonction demandée, question, appels déjà faits et leurs résultats.</summary>
    private sealed class ConversationState
    {
        private readonly Dictionary<string, (string Content, bool IsError)> _results = [];
        private readonly int _round;

        public string Feature { get; }
        public string Question { get; }
        public string? InvoiceId { get; }

        public ConversationState(EngineRequest request)
        {
            Feature = Regex.Match(request.Context, @"Fonction demandée : (\w+)").Groups[1].Value;
            var invoice = Regex.Match(request.Context, @"facture_id\) : ([0-9a-f-]{36})");
            InvoiceId = invoice.Success ? invoice.Groups[1].Value : null;
            // Seule la question compte : le contenu importé (<donnees_importees>) est ignoré.
            var first = request.Messages[0].Parts.OfType<TextPart>().First().Text;
            Question = first.Split("<donnees_importees>")[0].Trim();

            var names = new Dictionary<string, string>();
            foreach (var message in request.Messages)
            {
                foreach (var part in message.Parts)
                {
                    if (part is ToolCallPart call) names[call.Id] = call.Name;
                    if (part is ToolResultPart result && names.TryGetValue(result.ToolCallId, out var name))
                        _results[name] = (result.Content, result.IsError);
                }
            }
            _round = request.Messages.Count;
        }

        public (string Content, bool IsError)? Result(string tool) => _results.TryGetValue(tool, out var r) ? r : null;

        public EngineTurn Call(string tool, object input) =>
            new([new ToolCallPart($"appel_{_round}_{tool}", tool, JsonSerializer.SerializeToElement(input))], EngineStopReason.ToolUse, new EngineUsage(0, 0, 0, 0));
    }
}
