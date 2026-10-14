using System.Globalization;
using System.Text.RegularExpressions;

namespace Lettra.Ai.Orchestration;

/// <summary>
/// Contrôle de la réponse avant affichage (ADR-0012) : pas d'image ni de lien distant (ferme la fuite par URL),
/// et chaque montant en euros doit provenir d'un outil ou de la question, sinon il est signalé.
/// </summary>
internal static partial class ResponseGuard
{
    public static readonly IReadOnlyList<string> AllowedLinkHosts =
        ["impots.gouv.fr", "bofip.impots.gouv.fr", "service-public.fr", "urssaf.fr", "legifrance.gouv.fr"];

    public static string Sanitize(string text)
    {
        text = MarkdownImage().Replace(text, "");
        text = HtmlTag().Replace(text, "");
        text = MarkdownLink().Replace(text, m => IsAllowed(m.Groups["url"].Value)
            ? m.Value
            : m.Groups["label"].Value);
        text = BareUrl().Replace(text, m => IsAllowed(m.Value) ? m.Value : "[lien retiré]");
        return text;
    }

    private static bool IsAllowed(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.Query)
        && AllowedLinkHosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("www." + h, StringComparison.OrdinalIgnoreCase));

    /// <summary>Montants en euros de la réponse qu'on ne retrouve dans aucune source fiable (outils, question).</summary>
    public static IReadOnlyList<string> UngroundedAmounts(string answer, IEnumerable<string> trustedSources)
    {
        var known = new HashSet<decimal>();
        foreach (var source in trustedSources)
        {
            foreach (Match m in AnyNumber().Matches(source))
            {
                if (TryParse(m.Value, out var d))
                {
                    known.Add(Math.Abs(d));
                }
            }
        }

        return EuroAmount().Matches(answer)
            .Select(m => m.Groups["n"].Value)
            .Where(raw => TryParse(raw, out var d) && d != 0 && !known.Contains(Math.Abs(d)))
            .Distinct()
            .ToList();
    }

    private static bool TryParse(string raw, out decimal value)
    {
        var s = raw.Replace(" ", "").Replace(" ", "").Replace(" ", "");
        // « 1.234,56 » ou « 1 234,56 » (français) ; « 1234.56 » (JSON).
        if (s.Contains(','))
        {
            s = s.Replace(".", "").Replace(',', '.');
        }

        return decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")] private static partial Regex MarkdownImage();
    [GeneratedRegex(@"</?(img|iframe|script|a|object|embed|link|style)\b[^>]*>", RegexOptions.IgnoreCase)] private static partial Regex HtmlTag();
    [GeneratedRegex(@"\[(?<label>[^\]]*)\]\((?<url>[^)\s]*)[^)]*\)")] private static partial Regex MarkdownLink();
    [GeneratedRegex(@"(?<![\w(])(?:https?|ftp)://[^\s)\]>""']+", RegexOptions.IgnoreCase)] private static partial Regex BareUrl();
    [GeneratedRegex(@"-?\d[\d   .]*(?:,\d+)?")] private static partial Regex AnyNumber();
    [GeneratedRegex(@"(?<n>-?\d[\d   .]*(?:,\d{1,2})?)\s?(?:€|EUR|euros?)\b?", RegexOptions.IgnoreCase)] private static partial Regex EuroAmount();
}
