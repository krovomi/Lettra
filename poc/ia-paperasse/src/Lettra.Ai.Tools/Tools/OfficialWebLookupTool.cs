using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lettra.Ai.Abstractions;

namespace Lettra.Ai.Tools.Tools;

/// <summary>
/// Consultation d'une page des sites officiels (annexe B §7). Liste blanche de domaines, HTTPS, pas de paramètres
/// d'URL : un modèle manipulé ne peut ni joindre une autre adresse ni y glisser des données. Le cabinet peut la couper.
/// </summary>
public sealed partial class OfficialWebLookupTool(HttpClient http) : IAiTool
{
    public static readonly IReadOnlyList<string> AllowedHosts =
        ["impots.gouv.fr", "bofip.impots.gouv.fr", "service-public.fr", "urssaf.fr", "legifrance.gouv.fr"];

    private const int MaxChars = 20_000;

    public string Name => "consulter_site_officiel";
    public string Description =>
        "Lit une page d'un site officiel (impots.gouv.fr, bofip.impots.gouv.fr, service-public.fr, urssaf.fr, legifrance.gouv.fr) " +
        "pour vérifier une donnée susceptible d'avoir changé. Adresse https sans paramètres.";
    public AiToolRisk Risk => AiToolRisk.ReadOnly;
    public string RequiredPermission => AiPermissions.UseAssistant;
    public IReadOnlySet<AiFeature> Features { get; } = new HashSet<AiFeature> { AiFeature.RegulatoryQuestions, AiFeature.EInvoicingDiagnosis };
    public bool ReceivesRealValues => false;
    public JsonElement InputSchema { get; } = Schema.Object(("url", Schema.String("Adresse https d'une page officielle"), true)).ToElement();

    public static string? Reject(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "adresse invalide";
        if (uri.Scheme != Uri.UriSchemeHttps) return "seul https est accepté";
        if (!uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)) return "port ou identifiants interdits";
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return "paramètres d'URL interdits";
        var host = uri.IdnHost.ToLowerInvariant();
        if (!AllowedHosts.Any(h => host == h || host == "www." + h)) return $"domaine non autorisé : {host}";
        if (!SafePath().IsMatch(uri.AbsolutePath)) return "chemin non autorisé";
        return null;
    }

    public async Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken ct = default)
    {
        if (!context.Settings.OfficialWebLookup) return AiToolResult.Error("La consultation des sites officiels est désactivée par le cabinet.");
        var url = input.GetProperty("url").GetString() ?? "";
        if (Reject(url) is { } reason) return AiToolResult.Error($"Adresse refusée ({reason}).");

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode != HttpStatusCode.OK) return AiToolResult.Error($"Page indisponible ({(int)response.StatusCode}).");
        var html = await response.Content.ReadAsStringAsync(ct);
        var text = Whitespace().Replace(WebUtility.HtmlDecode(Tags().Replace(Scripts().Replace(html, " "), " ")), " ").Trim();
        return new AiToolResult(text.Length > MaxChars ? text[..MaxChars] : text) { Citation = url };
    }

    [GeneratedRegex(@"^/[A-Za-z0-9/_.\-]*\z")] private static partial Regex SafePath();
    [GeneratedRegex(@"<(script|style)[\s\S]*?</\1>", RegexOptions.IgnoreCase)] private static partial Regex Scripts();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex Tags();
    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();
}
