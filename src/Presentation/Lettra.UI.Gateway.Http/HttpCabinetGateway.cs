using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Common;
using Lettra.Contracts.Dossiers;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpCabinetGateway(HttpClient http) : ICabinetGateway
{
    public Task<OperationResult<IReadOnlyList<DossierSummary>>> ListDossiersAsync(CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, "api/v1/dossiers", null, ContractsJsonContext.Default.IReadOnlyListDossierSummary, cancellationToken);

    public Task<OperationResult<CabinetOverviewResponse>> GetOverviewAsync(CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, "api/v1/dossiers/overview", null, ContractsJsonContext.Default.CabinetOverviewResponse, cancellationToken);

    public Task<OperationResult<CabinetOverviewRefreshResponse>> RefreshOverviewAsync(Guid? dossierId, CancellationToken cancellationToken) =>
        SendAsync(
            HttpMethod.Post,
            dossierId is Guid id ? string.Create(CultureInfo.InvariantCulture, $"api/v1/dossiers/overview/refresh?dossier={id}") : "api/v1/dossiers/overview/refresh",
            null,
            ContractsJsonContext.Default.CabinetOverviewRefreshResponse,
            cancellationToken);

    public Task<OperationResult<DossierReviewResponse>> GetReviewAsync(string period, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, $"api/v1/review/{Uri.EscapeDataString(period)}", null, ContractsJsonContext.Default.DossierReviewResponse, cancellationToken);

    public Task<OperationResult<DossierReviewResponse>> SetReviewItemAsync(string period, string item, SetDossierReviewItemRequest request, CancellationToken cancellationToken) =>
        SendAsync(
            HttpMethod.Put,
            $"api/v1/review/{Uri.EscapeDataString(period)}/items/{Uri.EscapeDataString(item)}",
            JsonContent.Create(request, ContractsJsonContext.Default.SetDossierReviewItemRequest),
            ContractsJsonContext.Default.DossierReviewResponse,
            cancellationToken);

    private async Task<OperationResult<T>> SendAsync<T>(HttpMethod method, string path, HttpContent? content, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        try
        {
            using (content)
            {
                using HttpRequestMessage request = new(method, new Uri(path, UriKind.Relative)) { Content = content };
                using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                return await response.ToOperationResultAsync(typeInfo, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (HttpCalls.IsUnreachable(e, cancellationToken))
        {
            return OperationResult<T>.Failure(ErrorCodes.Api.Unreachable);
        }
    }
}
