using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.Contracts.Vigilance;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpThirdPartyChecksGateway(HttpClient http) : IThirdPartyChecksGateway
{
    public Task<OperationResult<ThirdPartyChecksResponse>> ListAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.GetAsync(HttpCalls.Relative($"api/v1/third-party-checks"), ct), ContractsJsonContext.Default.ThirdPartyChecksResponse, cancellationToken);

    public Task<OperationResult<ThirdPartyCheckRow>> CheckAsync(ThirdPartyKindDto kind, Guid partyId, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsync(HttpCalls.Relative($"api/v1/third-party-checks/{kind}/{partyId}"), null, ct), ContractsJsonContext.Default.ThirdPartyCheckRow, cancellationToken);
}
