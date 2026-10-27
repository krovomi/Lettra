using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.Contracts.Tax;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpFeeReturnGateway(HttpClient http) : IFeeReturnGateway
{
    public Task<OperationResult<FeeReturnResponse>> GetAsync(DateOnly? date, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(date is { } d ? HttpCalls.Relative($"api/v1/tax/fees?date={d:yyyy-MM-dd}") : HttpCalls.Relative($"api/v1/tax/fees"), ct),
            ContractsJsonContext.Default.FeeReturnResponse,
            cancellationToken);

    public Task<OperationResult<FeeReturnResponse>> SaveAsync(Guid supplierId, SaveFeeBeneficiaryRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        VersionedCalls.SendAsync(
            http, HttpMethod.Put, HttpCalls.Relative($"api/v1/tax/fees/{supplierId}"), expectedVersion,
            JsonContent.Create(request, ContractsJsonContext.Default.SaveFeeBeneficiaryRequest), ContractsJsonContext.Default.FeeReturnResponse, cancellationToken);

    public Task<OperationResult<FeeReturnResponse>> RemoveAsync(Guid supplierId, DateOnly fiscalYearStart, long expectedVersion, CancellationToken cancellationToken) =>
        VersionedCalls.SendAsync(
            http, HttpMethod.Delete, HttpCalls.Relative($"api/v1/tax/fees/{supplierId}?date={fiscalYearStart:yyyy-MM-dd}"), expectedVersion,
            null, ContractsJsonContext.Default.FeeReturnResponse, cancellationToken);
}
