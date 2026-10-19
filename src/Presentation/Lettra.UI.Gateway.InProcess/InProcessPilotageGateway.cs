using Lettra.Application.Features.Kpi;
using Lettra.Contracts.Common;
using Lettra.Contracts.Pilotage;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>
/// Mêmes cas d'usage que les routes <c>/pilotage</c> de l'API. Le tableau du dossier s'exécute dans le dossier ouvert ;
/// les deux autres hors de tout dossier (sur un poste sans collecte, ils répondent « indisponible »).
/// </summary>
internal sealed class InProcessPilotageGateway(IServiceScopeFactory scopes) : IPilotageGateway
{
    public Task<OperationResult<DossierPilotageResponse>> GetDossierAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetDossierPilotageQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<CabinetPilotageResponse>> GetCabinetAsync(CancellationToken cancellationToken) =>
        OutsideDossierAsync(sp => InProcessScope.QueryAsync(sp, new GetCabinetPilotageQuery(), cancellationToken));

    public Task<OperationResult<AdministrationPilotageResponse>> GetAdministrationAsync(CancellationToken cancellationToken) =>
        OutsideDossierAsync(sp => InProcessScope.QueryAsync(sp, new GetAdministrationPilotageQuery(), cancellationToken));

    private async Task<OperationResult<T>> OutsideDossierAsync<T>(Func<IServiceProvider, Task<OperationResult<T>>> operation)
    {
        AsyncServiceScope scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            return await operation(scope.ServiceProvider).ConfigureAwait(false);
        }
    }
}
