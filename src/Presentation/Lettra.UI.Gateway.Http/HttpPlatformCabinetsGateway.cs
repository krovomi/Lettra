using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Platform;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

/// <summary>Routes <c>/api/v1/plateforme/…</c>.</summary>
internal sealed class HttpPlatformCabinetsGateway(HttpClient http) : IPlatformCabinetsGateway
{
    private static ContractsJsonContext Json => ContractsJsonContext.Default;

    public Task<OperationResult<PlatformOverviewResponse>> GetOverviewAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.GetAsync(HttpCalls.Relative($"api/v1/plateforme/cabinets"), ct), Json.PlatformOverviewResponse, cancellationToken);

    public Task<OperationResult<OnboardCabinetResponse>> OnboardAsync(OnboardCabinetRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync(HttpCalls.Relative($"api/v1/plateforme/cabinets"), request, Json.OnboardCabinetRequest, ct),
            Json.OnboardCabinetResponse,
            cancellationToken);

    public Task<OperationResult<PlatformCabinetResponse>> ScheduleDepartureAsync(Guid cabinetId, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsync(HttpCalls.Relative($"api/v1/plateforme/cabinets/{cabinetId}/depart"), null, ct),
            Json.PlatformCabinetResponse,
            cancellationToken);

    public Task<OperationResult<PlatformCabinetResponse>> CancelDepartureAsync(Guid cabinetId, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.DeleteAsync(HttpCalls.Relative($"api/v1/plateforme/cabinets/{cabinetId}/depart"), ct),
            Json.PlatformCabinetResponse,
            cancellationToken);

    public Task<OperationResult<DossierRetryResponse>> RetryFailedDossiersAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsync(HttpCalls.Relative($"api/v1/plateforme/dossiers/relance"), null, ct),
            Json.DossierRetryResponse,
            cancellationToken);
}
