using Lettra.Contracts.Common;
using Lettra.Contracts.Pilotage;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Tableaux de bord : le dossier ouvert (calculé à l'instant), les dossiers que l'utilisateur peut ouvrir et ceux qu'il
/// administre (dernière collecte du serveur). Chaque appel n'en rend que ce que les droits permettent.
/// </summary>
public interface IPilotageGateway
{
    Task<OperationResult<DossierPilotageResponse>> GetDossierAsync(CancellationToken cancellationToken);

    Task<OperationResult<CabinetPilotageResponse>> GetCabinetAsync(CancellationToken cancellationToken);

    Task<OperationResult<AdministrationPilotageResponse>> GetAdministrationAsync(CancellationToken cancellationToken);
}
