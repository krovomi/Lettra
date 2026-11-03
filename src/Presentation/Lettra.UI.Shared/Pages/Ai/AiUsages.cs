using Lettra.Contracts.Assistant;

namespace Lettra.UI.Shared.Pages.Ai;

/// <summary>
/// Un usage de l'IA dans Lettra, pour le registre de la page « Bien utiliser l'IA de Lettra ». Les textes sont dans
/// <c>Localization/Modules/Ia[.en].resx</c> sous <c>AiGuide.Usage.&lt;Id&gt;.Name</c> et <c>.Role</c>.
/// </summary>
/// <param name="Id">Identifiant stable (clés de ressources, attribut <c>data-usage</c>).</param>
/// <param name="Feature">Fonction de l'assistant qui porte cet usage ; aucune pour la dictée, réglée à part.</param>
/// <param name="MinimalRisk">Risque minimal au sens du règlement européen sur l'IA ; sinon risque limité (obligation de transparence).</param>
public sealed record AiUsage(string Id, AssistantFeature? Feature, bool MinimalRisk = false)
{
    public string NameKey => $"AiGuide.Usage.{Id}.Name";

    public string RoleKey => $"AiGuide.Usage.{Id}.Role";

    public string RiskKey => MinimalRisk ? "AiGuide.Risk.Minimal" : "AiGuide.Risk.Limited";

    /// <summary>
    /// État de l'usage dans le dossier ouvert, d'après les fonctions que le serveur offre. Le brouillon de devis non offert
    /// est fermé par Lettra tant que le repérage des noms propres manque ; l'assistant du poste et la dictée se règlent
    /// ailleurs que dans le panneau.
    /// </summary>
    public string StatusKey(IReadOnlyCollection<AssistantFeature> offered)
    {
        ArgumentNullException.ThrowIfNull(offered);
        return Feature switch
        {
            null => "AiGuide.Usage.Status.Speech",
            AssistantFeature.ExternalAssistant => "AiGuide.Usage.Status.External",
            { } feature when offered.Contains(feature) => "AiGuide.Usage.Status.Open",
            AssistantFeature.DraftFromText => "AiGuide.Usage.Status.Blocked",
            _ => "AiGuide.Usage.Status.Closed",
        };
    }
}

/// <summary>
/// Le registre des usages de l'IA dans Lettra, montré dans l'application (maîtrise de l'IA, article 4 du règlement
/// européen sur l'IA) et repris dans les annexes. Un nouvel usage de l'IA ajoute sa ligne ici.
/// </summary>
public static class AiUsages
{
    public static IReadOnlyList<AiUsage> All { get; } =
    [
        new("explain", AssistantFeature.ExplainCompliance),
        new("einvoicing", AssistantFeature.EInvoicingDiagnosis),
        new("regulatory", AssistantFeature.RegulatoryQuestions),
        new("draft", AssistantFeature.DraftFromText),
        new("external", AssistantFeature.ExternalAssistant),
        new("speech", Feature: null, MinimalRisk: true),
    ];
}
