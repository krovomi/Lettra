using Lettra.Contracts.Assistant;

namespace Lettra.UI.Shared.Pages.Ai;

/// <summary>
/// Un usage de l'IA dans Lettra, pour le registre de la page « Bien utiliser l'IA de Lettra ». Les textes sont dans
/// <c>Localization/Modules/Ia[.en].resx</c> sous <c>AiGuide.Usage.&lt;Id&gt;.Name</c> et <c>.Role</c>.
/// </summary>
/// <param name="Id">Identifiant stable (clés de ressources, attribut <c>data-usage</c>).</param>
/// <param name="Feature">Fonction de l'assistant qui porte cet usage ; aucune pour un modèle qui tourne sur l'appareil ou le serveur du cabinet (dictée, lecture des pièces).</param>
/// <param name="MinimalRisk">Risque minimal au sens du règlement européen sur l'IA ; sinon risque limité (obligation de transparence).</param>
public sealed record AiUsage(string Id, AssistantFeature? Feature, bool MinimalRisk = false)
{
    public string NameKey => $"AiGuide.Usage.{Id}.Name";

    public string RoleKey => $"AiGuide.Usage.{Id}.Role";

    public string RiskKey => MinimalRisk ? "AiGuide.Risk.Minimal" : "AiGuide.Risk.Limited";

    /// <summary>
    /// État de l'usage dans le dossier ouvert, d'après les fonctions que le serveur offre : <c>open</c>, <c>closed</c>,
    /// <c>blocked</c> (le brouillon de devis, fermé par Lettra tant que le repérage des noms propres manque),
    /// <c>external</c> (l'assistant du poste, réglé par le cabinet) ou <c>device</c> (un modèle de l'appareil, sans
    /// fonction de l'assistant).
    /// </summary>
    public string Status(IReadOnlyCollection<AssistantFeature> offered)
    {
        ArgumentNullException.ThrowIfNull(offered);
        return Feature switch
        {
            null => "device",
            AssistantFeature.ExternalAssistant => "external",
            { } feature when offered.Contains(feature) => "open",
            AssistantFeature.DraftFromText => "blocked",
            _ => "closed",
        };
    }

    /// <summary>Texte de l'état : commun aux fonctions de l'assistant, propre à l'usage pour un modèle de l'appareil.</summary>
    public string StatusKey(IReadOnlyCollection<AssistantFeature> offered)
    {
        string status = Status(offered);
        return status == "device" ? $"AiGuide.Usage.{Id}.Status" : $"AiGuide.Usage.Status.{status}";
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
        new("receipts", Feature: null, MinimalRisk: true),
    ];
}
