using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpPreventionGateway(HttpClient http) : IPreventionGateway
{
    private static ContractsJsonContext Json => ContractsJsonContext.Default;

    public Task<OperationResult<PreventionRadarResponse>> GetRadarAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.GetAsync(HttpCalls.Relative($"api/v1/insights/prevention"), ct), Json.PreventionRadarResponse, cancellationToken);

    public Task<OperationResult<PreventionSummaryResponse>> GetSummaryAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.GetAsync(HttpCalls.Relative($"api/v1/insights/prevention/summary"), ct), Json.PreventionSummaryResponse, cancellationToken);

    public Task<OperationResult<PreventionSettingsResponse>> UpdateSettingsAsync(UpdatePreventionSettingsRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PutAsJsonAsync(HttpCalls.Relative($"api/v1/insights/prevention/settings"), request, Json.UpdatePreventionSettingsRequest, ct),
            Json.PreventionSettingsResponse,
            cancellationToken);

    public Task<OperationResult<PreventionLetterResponse>> GetLetterAsync(string? language, CancellationToken cancellationToken)
    {
        string query = string.IsNullOrWhiteSpace(language) ? string.Empty : "?language=" + Uri.EscapeDataString(language.Trim());
        return HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/insights/prevention/letter{query}"), ct), Json.PreventionLetterResponse, cancellationToken);
    }

    public Task<OperationResult<bool>> SendLetterAsync(SendPreventionLetterRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync(HttpCalls.Relative($"api/v1/insights/prevention/letter"), request, Json.SendPreventionLetterRequest, ct),
            (response, ct) => response.IsSuccessStatusCode ? Task.FromResult(OperationResult<bool>.Success(true)) : response.FailureAsync<bool>(ct),
            cancellationToken);
}
