using Lettra.Application.Features.Cabinet;
using Lettra.Contracts.Common;
using Lettra.Contracts.Missions;
using Lettra.Domain.Missions;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Mêmes cas d'usage et mêmes permissions que les routes <c>/mission</c> de l'API.</summary>
internal sealed class InProcessMissionGateway(IServiceScopeFactory scopes) : IMissionGateway
{
    public Task<OperationResult<MissionOverviewResponse>> GetAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetMissionOverviewQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<MissionOverviewResponse>> SaveAsync(SaveMissionRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessScope.WriteThenReadAsync(sp, new SaveMissionCommand(dossier, request.ToDraft(), expectedVersion), new GetMissionOverviewQuery(dossier), ct),
            cancellationToken);

    public Task<OperationResult<MissionOverviewResponse>> AddTimeAsync(AddTimeEntryRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessScope.SendAsync(sp, new AddTimeEntryCommand(dossier, request.Date, request.Minutes, (TimeTask)(int)request.Task, request.Note), ct),
            cancellationToken);

    public Task<OperationResult<MissionOverviewResponse>> DeleteTimeAsync(Guid id, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new DeleteTimeEntryCommand(dossier, id), ct), cancellationToken);
}
