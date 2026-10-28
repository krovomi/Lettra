using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.Contracts.Vigilance;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpVigilanceGateway(HttpClient http) : IVigilanceGateway
{
    public Task<OperationResult<VigilanceOverviewResponse>> GetAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.GetAsync(HttpCalls.Relative($"api/v1/vigilance"), ct), ContractsJsonContext.Default.VigilanceOverviewResponse, cancellationToken);

    public Task<OperationResult<VigilanceOverviewResponse>> SaveAsync(SaveDueDiligenceRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        VersionedCalls.SendAsync(
            http, HttpMethod.Put, HttpCalls.Relative($"api/v1/vigilance"), expectedVersion,
            JsonContent.Create(request, ContractsJsonContext.Default.SaveDueDiligenceRequest), ContractsJsonContext.Default.VigilanceOverviewResponse, cancellationToken);

    public Task<OperationResult<RegisteredOwnersResponse>> LookupOwnersAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/vigilance/registered-owners"), ct), ContractsJsonContext.Default.RegisteredOwnersResponse, cancellationToken);
}
