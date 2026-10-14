using System.Text.RegularExpressions;

namespace Lettra.Infrastructure.Observability;

/// <summary>
/// Dernier filet avant l'export : retire des signaux ce qui ressemble à une donnée de dossier (e-mail, IBAN,
/// SIREN, SIRET, numéro de TVA). La règle reste « ne jamais y mettre de données » (annexe G §2) ; ce filtre
/// et le processeur <c>redaction</c> du collecteur rattrapent les oublis.
/// </summary>
internal static partial class DossierDataScrubber
{
    public const string Mask = "***";

    /// <summary>Attributs qui peuvent porter une saisie et ne sortent jamais, quelle que soit leur valeur.</summary>
    public static readonly IReadOnlySet<string> DroppedAttributes = new HashSet<string>(StringComparer.Ordinal)
    {
        "url.query",
        "db.statement",
        "db.query.text",
        "exception.message",
        "exception.stacktrace",
        "http.request.body",
        "enduser.id",
    };

    public static bool MustDrop(string key) =>
        DroppedAttributes.Contains(key)
        || key.StartsWith("http.request.header.", StringComparison.Ordinal)
        || key.StartsWith("http.response.header.", StringComparison.Ordinal);

    public static string Scrub(string value) =>
        string.IsNullOrEmpty(value) ? value : SensitivePattern().Replace(value, Mask);

    public static bool LooksSensitive(string value) =>
        !string.IsNullOrEmpty(value) && SensitivePattern().IsMatch(value);

    // E-mail ; IBAN (2 lettres, 2 chiffres, 11 à 30 alphanumériques) ; TVA intracommunautaire française ;
    // suites de 9 (SIREN) ou 14 (SIRET) chiffres isolées, éventuellement groupées par espaces.
    [GeneratedRegex(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}"
        + @"|\b[A-Z]{2}\d{2}(?:\s?[A-Z0-9]{4}){2,7}(?:\s?[A-Z0-9]{1,4})?\b"
        + @"|\bFR\s?[0-9A-Z]{2}\s?\d{9}\b"
        + @"|(?<![\d-])\d{3}\s?\d{3}\s?\d{3}(?:\s?\d{5})?(?![\d-])",
        RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex SensitivePattern();
}
