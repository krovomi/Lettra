using System.Globalization;
using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.Contracts.Suppliers;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

/// <summary>Routes <c>/suppliers/iban-changes</c> et <c>/suppliers/{id}/iban-changes</c>.</summary>
internal sealed class HttpIbanChangesGateway(HttpClient http) : IIbanChangesGateway
{
    public Task<OperationResult<PagedResponse<IbanChangeResponse>>> ListAsync(IbanChangeListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        string status = query.Status is { } s ? $"&status={s}" : string.Empty;
        string sort = query.Sort != IbanChangeSortDto.ChangedAt || query.Direction != SortDirectionDto.Descending
            ? $"&sort={query.Sort}&direction={query.Direction}"
            : string.Empty;
        string page = string.Create(CultureInfo.InvariantCulture, $"page={query.Page}&pageSize={query.PageSize}");
        return HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/suppliers/iban-changes?{page}{status}{HttpCalls.SearchParameter(query.Search)}{sort}"), ct),
            ContractsJsonContext.Default.PagedResponseIbanChangeResponse,
            cancellationToken);
    }

    public Task<OperationResult<IReadOnlyList<IbanChangeResponse>>> ForSupplierAsync(Guid supplierId, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/suppliers/{supplierId}/iban-changes"), ct),
            ContractsJsonContext.Default.IReadOnlyListIbanChangeResponse,
            cancellationToken);

    public Task<OperationResult<bool>> ConfirmAsync(Guid changeId, ConfirmIbanChangeRequest request, CancellationToken cancellationToken) =>
        NoContentAsync(
            ct => http.PostAsJsonAsync(HttpCalls.Relative($"api/v1/suppliers/iban-changes/{changeId}/confirmation"), request, ContractsJsonContext.Default.ConfirmIbanChangeRequest, ct),
            cancellationToken);

    public Task<OperationResult<bool>> RejectAsync(Guid changeId, RejectIbanChangeRequest request, CancellationToken cancellationToken) =>
        NoContentAsync(
            ct => http.PostAsJsonAsync(HttpCalls.Relative($"api/v1/suppliers/iban-changes/{changeId}/rejection"), request, ContractsJsonContext.Default.RejectIbanChangeRequest, ct),
            cancellationToken);

    private static Task<OperationResult<bool>> NoContentAsync(Func<CancellationToken, Task<HttpResponseMessage>> send, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            send,
            (response, ct) => response.IsSuccessStatusCode ? Task.FromResult(OperationResult<bool>.Success(true)) : response.FailureAsync<bool>(ct),
            cancellationToken);
}
