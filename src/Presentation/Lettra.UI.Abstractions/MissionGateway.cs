using Lettra.Contracts.Common;
using Lettra.Contracts.Missions;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Lettre de mission, honoraires et temps passé du dossier. Lire demande <c>DossierLire</c> ; changer la mission,
/// <c>FactureValider</c> ; saisir ou supprimer son temps, <c>FactureSaisir</c>. Refus possibles : <c>Mission.*</c>,
/// <c>Concurrency.VersionMismatch</c>.
/// </summary>
public interface IMissionGateway
{
    Task<OperationResult<MissionOverviewResponse>> GetAsync(CancellationToken cancellationToken);

    /// <summary>Crée la mission (<paramref name="expectedVersion"/> vide) ou la modifie à partir de la version lue.</summary>
    Task<OperationResult<MissionOverviewResponse>> SaveAsync(SaveMissionRequest request, long? expectedVersion, CancellationToken cancellationToken);

    Task<OperationResult<MissionOverviewResponse>> AddTimeAsync(AddTimeEntryRequest request, CancellationToken cancellationToken);

    Task<OperationResult<MissionOverviewResponse>> DeleteTimeAsync(Guid id, CancellationToken cancellationToken);
}
