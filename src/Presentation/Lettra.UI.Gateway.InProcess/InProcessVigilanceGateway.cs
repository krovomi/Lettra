using Lettra.Application.Features.Cabinet;
using Lettra.Application.Features.Parties;
using Lettra.Contracts.Common;
using Lettra.Contracts.Vigilance;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Mêmes cas d'usage et mêmes permissions que les routes <c>/vigilance</c> de l'API.</summary>
internal sealed class InProcessVigilanceGateway(IServiceScopeFactory scopes) : IVigilanceGateway
{
    public Task<OperationResult<VigilanceOverviewResponse>> GetAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetVigilanceQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<VigilanceOverviewResponse>> SaveAsync(SaveDueDiligenceRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessScope.WriteThenReadAsync(sp, new SaveVigilanceCommand(dossier, request.ToDraft(), expectedVersion), new GetVigilanceQuery(dossier), ct),
            cancellationToken);

    public Task<OperationResult<RegisteredOwnersResponse>> LookupOwnersAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new LookupRegisteredOwnersQuery(dossier), ct), cancellationToken);
}
