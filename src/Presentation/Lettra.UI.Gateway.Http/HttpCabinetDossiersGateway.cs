using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Dossiers;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

/// <summary>Routes <c>/api/v1/cabinets/{cabinet}/dossiers/…</c>.</summary>
internal sealed class HttpCabinetDossiersGateway(HttpClient http) : ICabinetDossiersGateway
{
    private static ContractsJsonContext Json => ContractsJsonContext.Default;

    public Task<OperationResult<IReadOnlyList<CabinetDossierResponse>>> ListAsync(Guid cabinetId, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.GetAsync(Dossiers(cabinetId), ct), Json.IReadOnlyListCabinetDossierResponse, cancellationToken);

    public Task<OperationResult<DossierSummary>> CreateAsync(Guid cabinetId, CreateDossierRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.PostAsJsonAsync(Dossiers(cabinetId), request, Json.CreateDossierRequest, ct), Json.DossierSummary, cancellationToken);

    public Task<OperationResult<DossierErasureResponse>> ScheduleErasureAsync(Guid cabinetId, Guid dossierId, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.PostAsync(Erasure(cabinetId, dossierId), null, ct), Json.DossierErasureResponse, cancellationToken);

    public Task<OperationResult<DossierSummary>> CancelErasureAsync(Guid cabinetId, Guid dossierId, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.DeleteAsync(Erasure(cabinetId, dossierId), ct), Json.DossierSummary, cancellationToken);

    private static Uri Dossiers(Guid cabinetId) => HttpCalls.Relative($"api/v1/cabinets/{cabinetId}/dossiers");

    private static Uri Erasure(Guid cabinetId, Guid dossierId) => HttpCalls.Relative($"api/v1/cabinets/{cabinetId}/dossiers/{dossierId}/effacement");
}
