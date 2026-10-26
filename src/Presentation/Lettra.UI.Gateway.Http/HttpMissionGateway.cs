using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Missions;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpMissionGateway(HttpClient http) : IMissionGateway
{
    public Task<OperationResult<MissionOverviewResponse>> GetAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.GetAsync(HttpCalls.Relative($"api/v1/mission"), ct), ContractsJsonContext.Default.MissionOverviewResponse, cancellationToken);

    public Task<OperationResult<MissionOverviewResponse>> SaveAsync(SaveMissionRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        VersionedCalls.SendAsync(
            http, HttpMethod.Put, HttpCalls.Relative($"api/v1/mission"), expectedVersion,
            JsonContent.Create(request, ContractsJsonContext.Default.SaveMissionRequest), ContractsJsonContext.Default.MissionOverviewResponse, cancellationToken);

    public Task<OperationResult<MissionOverviewResponse>> AddTimeAsync(AddTimeEntryRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync(HttpCalls.Relative($"api/v1/mission/time"), request, ContractsJsonContext.Default.AddTimeEntryRequest, ct),
            ContractsJsonContext.Default.MissionOverviewResponse,
            cancellationToken);

    public Task<OperationResult<MissionOverviewResponse>> DeleteTimeAsync(Guid id, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.DeleteAsync(HttpCalls.Relative($"api/v1/mission/time/{id}"), ct), ContractsJsonContext.Default.MissionOverviewResponse, cancellationToken);
}
