using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Radar de prévention des difficultés du dossier ouvert, réservé au cabinet. Lire et proposer un rendez-vous demandent
/// <c>PreventionConsulter</c> ; régler les seuils, <c>PreventionRegler</c>. La carte de l'accueil ne refuse jamais :
/// sans le droit, elle revient cachée.
/// </summary>
public interface IPreventionGateway
{
    /// <summary>Le niveau d'attention du dossier, chaque signal avec sa raison et ses chiffres, et les seuils.</summary>
    Task<OperationResult<PreventionRadarResponse>> GetRadarAsync(CancellationToken cancellationToken);

    /// <summary>La carte « Signaux de prévention » de l'accueil : niveau et signaux actifs, ou cachée sans le droit.</summary>
    Task<OperationResult<PreventionSummaryResponse>> GetSummaryAsync(CancellationToken cancellationToken);

    /// <summary>Enregistre les seuils du dossier ; sur un conflit de version, le refus <c>Concurrency.VersionMismatch</c>.</summary>
    Task<OperationResult<PreventionSettingsResponse>> UpdateSettingsAsync(UpdatePreventionSettingsRequest request, CancellationToken cancellationToken);

    /// <summary>Le courrier type de proposition de rendez-vous, dans la langue demandée (<c>fr-FR</c> par défaut).</summary>
    Task<OperationResult<PreventionLetterResponse>> GetLetterAsync(string? language, CancellationToken cancellationToken);

    /// <summary>Envoie le courrier relu par le collaborateur (refus <c>Email.NotConfigured</c> sans serveur d'e-mail).</summary>
    Task<OperationResult<bool>> SendLetterAsync(SendPreventionLetterRequest request, CancellationToken cancellationToken);
}
