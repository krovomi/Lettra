using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Preferences;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

/// <summary>
/// Routes <c>/api/v1/me/preferences</c>. Un serveur plus ancien n'a pas la route : il répond 404, 405 ou la page de
/// l'application (HTML) ; c'est traduit en <c>Preferences.Unavailable</c> pour que l'écran retombe sur l'appareil.
/// </summary>
internal sealed class HttpUserPreferencesGateway(HttpClient http) : IUserPreferencesGateway
{
    private const string Path = "api/v1/me/preferences";

    private static ContractsJsonContext Json => ContractsJsonContext.Default;

    public Task<OperationResult<UserPreferencesResponse>> GetAsync(CancellationToken cancellationToken) =>
        SendAsync(ct => http.GetAsync(HttpCalls.Relative($"{Path}"), ct), cancellationToken);

    public Task<OperationResult<UserPreferencesResponse>> SaveAsync(SaveUserPreferencesRequest request, CancellationToken cancellationToken) =>
        SendAsync(ct => http.PutAsJsonAsync(HttpCalls.Relative($"{Path}"), request, Json.SaveUserPreferencesRequest, ct), cancellationToken);

    public Task<OperationResult<UserPreferencesResponse>> RecordUsageAsync(RecordUsageRequest request, CancellationToken cancellationToken) =>
        SendAsync(ct => http.PostAsJsonAsync(HttpCalls.Relative($"{Path}/usage"), request, Json.RecordUsageRequest, ct), cancellationToken);

    public Task<OperationResult<UserPreferencesResponse>> ForgetUsageAsync(Guid? dossierId, CancellationToken cancellationToken) =>
        SendAsync(
            ct => http.DeleteAsync(dossierId is Guid id ? HttpCalls.Relative($"{Path}/usage?dossierId={id}") : HttpCalls.Relative($"{Path}/usage"), ct),
            cancellationToken);

    public Task<OperationResult<UserPreferencesResponse>> ResetAsync(CancellationToken cancellationToken) =>
        SendAsync(ct => http.DeleteAsync(HttpCalls.Relative($"{Path}"), ct), cancellationToken);

    private static async Task<OperationResult<UserPreferencesResponse>> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await send(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed || !IsJson(response))
            {
                return OperationResult<UserPreferencesResponse>.Failure(ErrorCodes.Preferences.Unavailable);
            }

            return await response.ToOperationResultAsync(Json.UserPreferencesResponse, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return OperationResult<UserPreferencesResponse>.Failure(ErrorCodes.Preferences.Unavailable);
        }
        catch (Exception e) when (HttpCalls.IsUnreachable(e, cancellationToken))
        {
            return OperationResult<UserPreferencesResponse>.Failure(ErrorCodes.Api.Unreachable);
        }
    }

    /// <summary>Une réponse de l'API (JSON ou problème RFC 9457), pas la page HTML servie par le repli de l'hôte.</summary>
    private static bool IsJson(HttpResponseMessage response) =>
        response.Content.Headers.ContentType?.MediaType is { } media && media.Contains("json", StringComparison.OrdinalIgnoreCase);
}
