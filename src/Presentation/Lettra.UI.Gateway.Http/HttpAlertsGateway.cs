using System.Globalization;
using System.Net.Http.Json;
using Lettra.Contracts.Alerts;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpAlertsGateway(HttpClient http) : IAlertsGateway
{
    public Task<OperationResult<AlertCentreResponse>> ListAsync(bool cabinetWide, bool includeClosed, CancellationToken cancellationToken) =>
        GetAsync(
            string.Create(CultureInfo.InvariantCulture, $"api/v1/alerts?cabinet={Flag(cabinetWide)}&includeClosed={Flag(includeClosed)}"),
            ContractsJsonContext.Default.AlertCentreResponse,
            cancellationToken);

    public Task<OperationResult<AlertDetailResponse>> GetAsync(Guid alertId, bool cabinetWide, CancellationToken cancellationToken) =>
        GetAsync(
            string.Create(CultureInfo.InvariantCulture, $"api/v1/alerts/{alertId}?cabinet={Flag(cabinetWide)}"),
            ContractsJsonContext.Default.AlertDetailResponse,
            cancellationToken);

    public Task<OperationResult<AlertDetailResponse>> AcknowledgeAsync(Guid alertId, CancellationToken cancellationToken) =>
        ActAsync(alertId, "acknowledge", null, cancellationToken);

    public Task<OperationResult<AlertDetailResponse>> ResolveAsync(Guid alertId, CancellationToken cancellationToken) =>
        ActAsync(alertId, "resolve", null, cancellationToken);

    public Task<OperationResult<AlertDetailResponse>> ChangeSeverityAsync(Guid alertId, ChangeAlertSeverityRequest request, CancellationToken cancellationToken) =>
        ActAsync(alertId, "severity", JsonContent.Create(request, ContractsJsonContext.Default.ChangeAlertSeverityRequest), cancellationToken);

    private static string Flag(bool value) => value ? "true" : "false";

    private async Task<OperationResult<T>> GetAsync<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await http.GetAsync(new Uri(path, UriKind.Relative), cancellationToken).ConfigureAwait(false);
            return await response.ToOperationResultAsync(typeInfo, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return OperationResult<T>.Failure(ErrorCodes.Api.Unreachable);
        }
    }

    private async Task<OperationResult<AlertDetailResponse>> ActAsync(Guid alertId, string action, HttpContent? content, CancellationToken cancellationToken)
    {
        try
        {
            using (content)
            {
                Uri uri = new(string.Create(CultureInfo.InvariantCulture, $"api/v1/alerts/{alertId}/{action}"), UriKind.Relative);
                using HttpResponseMessage response = await http.PostAsync(uri, content, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return await response.ToOperationResultAsync(ContractsJsonContext.Default.AlertDetailResponse, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (HttpRequestException)
        {
            return OperationResult<AlertDetailResponse>.Failure(ErrorCodes.Api.Unreachable);
        }

        return await GetAsync(alertId, cabinetWide: true, cancellationToken).ConfigureAwait(false);
    }
}
