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

    public static string Scrub(string value) => Scrub(value, SensitivePattern());

    public static bool LooksSensitive(string value) => LooksSensitive(value, SensitivePattern());

    // Délai dépassé (runner chargé, très longue valeur) : on masque tout plutôt que de laisser passer la valeur ou
    // de faire échouer l'appelant (une migration qui journalise son SQL, par exemple).
    internal static string Scrub(string value, Regex pattern)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        try
        {
            return pattern.Replace(value, Mask);
        }
        catch (RegexMatchTimeoutException)
        {
            return Mask;
        }
    }

    internal static bool LooksSensitive(string value, Regex pattern)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        try
        {
            return pattern.IsMatch(value);
        }
        catch (RegexMatchTimeoutException)
        {
            return true;
        }
    }

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
