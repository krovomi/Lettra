using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Common;
using Lettra.Contracts.Dossiers;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Vue cabinet : les dossiers que l'utilisateur peut ouvrir (sélecteur), leur tableau de supervision, et la revue du
/// dossier ouvert. In-process sur le poste isolé, HTTP ailleurs ; mêmes cas d'usage, mêmes codes d'erreur.
/// </summary>
public interface ICabinetGateway
{
    Task<OperationResult<IReadOnlyList<DossierSummary>>> ListDossiersAsync(CancellationToken cancellationToken);

    Task<OperationResult<CabinetOverviewResponse>> GetOverviewAsync(CancellationToken cancellationToken);

    /// <summary>Recalcule maintenant les indicateurs (tous les dossiers accessibles, ou <paramref name="dossierId"/>).</summary>
    Task<OperationResult<CabinetOverviewRefreshResponse>> RefreshOverviewAsync(Guid? dossierId, CancellationToken cancellationToken);

    /// <summary>La revue du dossier ouvert pour <paramref name="period"/> (<c>2026-09</c> ou <c>2026-T3</c>).</summary>
    Task<OperationResult<DossierReviewResponse>> GetReviewAsync(string period, CancellationToken cancellationToken);

    Task<OperationResult<DossierReviewResponse>> SetReviewItemAsync(string period, string item, SetDossierReviewItemRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Une sélection de dossier que l'utilisateur peut changer (sélecteur de dossier). L'hôte qui n'en fournit pas garde un
/// dossier fixe : le sélecteur affiche alors le dossier ouvert sans proposer d'en changer.
/// </summary>
public interface ISelectableDossierSelection : IDossierSelection
{
    Task SelectAsync(Guid dossierId, CancellationToken cancellationToken);
}

/// <summary>
/// Sélection du navigateur : le dossier de la configuration au démarrage, puis celui que l'utilisateur choisit (l'écran
/// le retient sur l'appareil et le rouvre au lancement suivant).
/// </summary>
public sealed class SwitchableDossierSelection(Guid? initial) : ISelectableDossierSelection
{
    public Guid? CurrentDossierId { get; private set; } = initial;

    public Task SelectAsync(Guid dossierId, CancellationToken cancellationToken)
    {
        CurrentDossierId = dossierId;
        return Task.CompletedTask;
    }
}
