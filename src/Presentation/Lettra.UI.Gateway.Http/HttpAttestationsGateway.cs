using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.Contracts.Vigilance;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpAttestationsGateway(HttpClient http) : IAttestationsGateway
{
    public Task<OperationResult<SupplierAttestationsResponse>> ListAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.GetAsync(HttpCalls.Relative($"api/v1/attestations"), ct), ContractsJsonContext.Default.SupplierAttestationsResponse, cancellationToken);

    public Task<OperationResult<SupplierAttestationsResponse>> SaveAsync(Guid supplierId, SaveAttestationRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        VersionedCalls.SendAsync(
            http, HttpMethod.Put, HttpCalls.Relative($"api/v1/attestations/{supplierId}"), expectedVersion,
            JsonContent.Create(request, ContractsJsonContext.Default.SaveAttestationRequest), ContractsJsonContext.Default.SupplierAttestationsResponse, cancellationToken);
}
