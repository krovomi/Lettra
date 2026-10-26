using System.Net;
using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Portal;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpPortalGateway(HttpClient http) : IPortalGateway
{
    public async Task<OperationResult<PortalLinkCreated>> CreateLinkAsync(CreatePortalLinkRequest request, CancellationToken cancellationToken)
    {
        OperationResult<PortalLinkResponse> created = await HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync(HttpCalls.Relative($"api/v1/portal-links"), request, ContractsJsonContext.Default.CreatePortalLinkRequest, ct),
            ContractsJsonContext.Default.PortalLinkResponse,
            cancellationToken).ConfigureAwait(false);

        // L'adresse du serveur est celle de l'API : le navigateur, comme l'application reliée à un serveur commun.
        return created.IsSuccess
            ? OperationResult<PortalLinkCreated>.Success(new PortalLinkCreated(created.Value!, new Uri(http.BaseAddress!, created.Value!.Path).ToString()))
            : OperationResult<PortalLinkCreated>.Failure(created.Errors);
    }

    public Task<OperationResult<IReadOnlyList<PortalLinkSummary>>> ListLinksAsync(Guid customerId, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/portal-links?customerId={customerId}"), ct),
            ContractsJsonContext.Default.IReadOnlyListPortalLinkSummary,
            cancellationToken);

    public Task<OperationResult<bool>> RevokeLinkAsync(Guid linkId, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsync(HttpCalls.Relative($"api/v1/portal-links/{linkId}/revoke"), null, ct),
            (response, ct) => response.IsSuccessStatusCode ? Task.FromResult(OperationResult<bool>.Success(true)) : response.FailureAsync<bool>(ct),
            cancellationToken);

    /// <summary>204 : le devis n'a pas été signé en ligne, le même refus que sur le poste.</summary>
    public Task<OperationResult<QuoteSignatureResponse>> GetQuoteSignatureAsync(Guid quoteId, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/quotes/{quoteId}/signature"), ct),
            (response, ct) => response.StatusCode == HttpStatusCode.NoContent
                ? Task.FromResult(OperationResult<QuoteSignatureResponse>.Failure(ErrorCodes.Portal.SignatureNotFound))
                : response.ToOperationResultAsync(ContractsJsonContext.Default.QuoteSignatureResponse, ct),
            cancellationToken);
}
