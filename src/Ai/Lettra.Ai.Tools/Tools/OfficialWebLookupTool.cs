using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Identity;
using Lettra.Contracts.Assistant;

namespace Lettra.Ai.Tools.Tools;

/// <summary>
/// Consultation d'une page des sites officiels (annexe B §7). Liste blanche de domaines, HTTPS, pas de paramètres
/// d'URL : un modèle manipulé ne peut ni joindre une autre adresse ni y glisser des données. Le cabinet peut la couper.
/// </summary>
internal sealed partial class OfficialWebLookupTool(HttpClient http) : IAiTool
{
    public const string ToolName = "consulter_site_officiel";

    public static readonly IReadOnlyList<string> AllowedHosts =
        ["impots.gouv.fr", "bofip.impots.gouv.fr", "service-public.fr", "urssaf.fr", "legifrance.gouv.fr"];

    private const int MaxChars = 20_000;

    public string Name => ToolName;

    public string Description =>
        "Lit une page d'un site officiel (impots.gouv.fr, bofip.impots.gouv.fr, service-public.fr, urssaf.fr, legifrance.gouv.fr) " +
        "pour vérifier une donnée susceptible d'avoir changé. Adresse https sans paramètres.";

    public AssistantToolRisk Risk => AssistantToolRisk.ReadOnly;

    public Permission? RequiredPermission => null;

    public IReadOnlySet<AssistantFeature> Features { get; } =
        new HashSet<AssistantFeature> { AssistantFeature.RegulatoryQuestions, AssistantFeature.EInvoicingDiagnosis };

    /// <summary>L'adresse sort du serveur : elle reste sur les jetons, rien du dossier ne peut partir dans l'URL.</summary>
    public bool ReceivesRealValues => false;

    public JsonElement InputSchema { get; } = Schema.Object(("url", Schema.String("Adresse https d'une page officielle"), true)).ToElement();

    public bool IsEnabledBy(AiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.OfficialWebLookup;
    }

    /// <summary>Raison du refus d'une adresse, ou <c>null</c> si elle est acceptée.</summary>
    public static string? Reject(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return "adresse invalide";
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return "seul https est accepté";
        }

        if (!uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return "port ou identifiants interdits";
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return "paramètres d'URL interdits";
        }

        string host = uri.IdnHost;
        if (!AllowedHosts.Any(h => host.Equals(h, StringComparison.OrdinalIgnoreCase) || host.Equals("www." + h, StringComparison.OrdinalIgnoreCase)))
        {
            return $"domaine non autorisé : {host}";
        }

        return SafePath().IsMatch(uri.AbsolutePath) ? null : "chemin non autorisé";
    }

    public async Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Settings.OfficialWebLookup)
        {
            return AiToolResult.Error("La consultation des sites officiels est désactivée par le cabinet.");
        }

        string url = input.GetProperty("url").GetString() ?? string.Empty;
        if (Reject(url) is { } reason)
        {
            return AiToolResult.Error($"Adresse refusée ({reason}).");
        }

        using HttpResponseMessage response = await http
            .GetAsync(new Uri(url), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return AiToolResult.Error($"Page indisponible ({(int)response.StatusCode}).");
        }

        string html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        string text = Whitespace().Replace(WebUtility.HtmlDecode(Tags().Replace(Scripts().Replace(html, " "), " ")), " ").Trim();
        return new AiToolResult(text.Length > MaxChars ? text[..MaxChars] : text) { Citation = url };
    }

    [GeneratedRegex(@"^/[A-Za-z0-9/_.\-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafePath();

    [GeneratedRegex(@"<(script|style)[\s\S]*?</\1>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Scripts();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
