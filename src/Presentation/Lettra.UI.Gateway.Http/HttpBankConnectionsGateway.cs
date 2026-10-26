using System.Net.Http.Json;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpBankConnectionsGateway(HttpClient http) : IBankConnectionsGateway
{
    public Task<OperationResult<BankConnectionsResponse>> GetAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.GetAsync(HttpCalls.Relative($"api/v1/bank/connections"), ct), ContractsJsonContext.Default.BankConnectionsResponse, cancellationToken);

    public Task<OperationResult<BankConsentRedirectResponse>> StartAsync(StartBankConnectionRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync("api/v1/bank/connections", request, ContractsJsonContext.Default.StartBankConnectionRequest, ct),
            ContractsJsonContext.Default.BankConsentRedirectResponse,
            cancellationToken);

    public Task<OperationResult<BankConnectionCompletedResponse>> CompleteAsync(Guid connectionId, CompleteBankConnectionRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync(
                HttpCalls.Relative($"api/v1/bank/connections/{connectionId}/complete"), request, ContractsJsonContext.Default.CompleteBankConnectionRequest, ct),
            ContractsJsonContext.Default.BankConnectionCompletedResponse,
            cancellationToken);

    public Task<OperationResult<SyncBankConnectionsResponse>> SyncAsync(SyncBankConnectionsRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync("api/v1/bank/connections/sync", request, ContractsJsonContext.Default.SyncBankConnectionsRequest, ct),
            ContractsJsonContext.Default.SyncBankConnectionsResponse,
            cancellationToken);

    public Task<OperationResult<BankConnectionResponse>> RevokeAsync(Guid connectionId, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsync(HttpCalls.Relative($"api/v1/bank/connections/{connectionId}/revoke"), null, ct),
            ContractsJsonContext.Default.BankConnectionResponse,
            cancellationToken);
}
