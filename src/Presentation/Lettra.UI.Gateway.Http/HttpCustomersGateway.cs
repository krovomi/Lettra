using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpCustomersGateway(HttpClient http) : ICustomersGateway
{
    public Task<OperationResult<CustomerResponse>> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync("api/v1/customers", request, ContractsJsonContext.Default.CreateCustomerRequest, ct),
            ContractsJsonContext.Default.CustomerResponse,
            cancellationToken);

    public Task<OperationResult<PagedResponse<CustomerSummary>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/customers?page={page}&pageSize={pageSize}"), ct),
            ContractsJsonContext.Default.PagedResponseCustomerSummary,
            cancellationToken);

    public Task<OperationResult<CustomerResponse>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/customers/{id}"), ct),
            ContractsJsonContext.Default.CustomerResponse,
            cancellationToken);

    public Task<OperationResult<PagedResponse<CustomerSummary>>> SearchAsync(PartyListQuery query, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(new Uri("api/v1/customers?" + HttpCalls.PartyListParameters(query), UriKind.Relative), ct),
            ContractsJsonContext.Default.PagedResponseCustomerSummary,
            cancellationToken);

    public Task<OperationResult<CustomerResponse>> UpdateAsync(Guid id, long expectedVersion, CreateCustomerRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PutVersionedAsync(HttpCalls.Relative($"api/v1/customers/{id}"), expectedVersion, request, ContractsJsonContext.Default.CreateCustomerRequest, ct),
            ContractsJsonContext.Default.CustomerResponse,
            cancellationToken);

    public Task<OperationResult<bool>> DeleteAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken) =>
        VersionedCalls.DeleteAsync(http, HttpCalls.Relative($"api/v1/customers/{id}"), expectedVersion, cancellationToken);
}
