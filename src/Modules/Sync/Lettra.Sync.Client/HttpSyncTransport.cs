using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.Contracts.Sync;

namespace Lettra.Sync.Client;

/// <summary>
/// Transport HTTP vers l'API (<c>/api/v1/sync/…</c>). Le <see cref="HttpClient"/> est fourni par l'hôte, avec son adresse
/// et son jeton (connexion PKCE du poste) ; le dossier part dans l'en-tête <c>X-Dossier</c> comme pour toute l'API, et le
/// poste dans <see cref="SyncHttp.DeviceHeader"/> (registre des postes du serveur, étape 4.4).
/// </summary>
public sealed class HttpSyncTransport(HttpClient http, SyncClientOptions options) : ISyncTransport
{
    public async Task<PushSyncIntentsResponse> PushAsync(Guid dossierId, PushSyncIntentsRequest request, CancellationToken cancellationToken)
    {
        using HttpRequestMessage message = new(HttpMethod.Post, new Uri(SyncHttp.IntentsPath, UriKind.Relative))
        {
            Content = JsonContent.Create(request, ContractsJsonContext.Default.PushSyncIntentsRequest),
        };
        using HttpResponseMessage response = await SendAsync(message, dossierId, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.PushSyncIntentsResponse, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<SyncChangesResponse> PullAsync(Guid dossierId, string? token, int limit, CancellationToken cancellationToken)
    {
        string query = string.Create(CultureInfo.InvariantCulture, $"{SyncHttp.ChangesPath}?limit={limit}");
        if (!string.IsNullOrEmpty(token))
        {
            query += "&token=" + Uri.EscapeDataString(token);
        }

        using HttpRequestMessage message = new(HttpMethod.Get, new Uri(query, UriKind.Relative));
        using HttpResponseMessage response = await SendAsync(message, dossierId, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.SyncChangesResponse, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<OpenDeviceSeriesResponse> OpenDeviceSeriesAsync(Guid dossierId, OpenDeviceSeriesRequest request, CancellationToken cancellationToken)
    {
        using HttpRequestMessage message = new(HttpMethod.Post, new Uri(SyncHttp.DeviceSeriesPath, UriKind.Relative))
        {
            Content = JsonContent.Create(request, ContractsJsonContext.Default.OpenDeviceSeriesRequest),
        };
        using HttpResponseMessage response = await SendAsync(message, dossierId, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.OpenDeviceSeriesResponse, cancellationToken).ConfigureAwait(false))!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, Guid dossierId, CancellationToken cancellationToken)
    {
        message.Headers.Add(LettraHeaders.Dossier, dossierId.ToString());
        message.Headers.Add(SyncHttp.DeviceHeader, options.DeviceId.ToString());
        HttpResponseMessage response = await http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static async Task EnsureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
                throw new SyncRefusedException(SyncFailure.Unauthenticated);
            case HttpStatusCode.Forbidden when await CodeAsync(response, cancellationToken).ConfigureAwait(false) == ErrorCodes.Sync.DeviceRevoked:
                throw new SyncRefusedException(SyncFailure.DeviceRevoked);
            case HttpStatusCode.Forbidden or HttpStatusCode.NotFound:
                throw new SyncRefusedException(SyncFailure.Forbidden);
            default:
                // 5xx, 429, passerelle : traité comme une coupure, on reprendra plus tard avec les mêmes clés.
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Synchronisation : réponse {(int)response.StatusCode}.", null, response.StatusCode);
                }

                break;
        }
    }

    /// <summary>Le code d'erreur du problème renvoyé par l'API (<c>code</c>), <c>null</c> si la réponse n'en porte pas.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
            return problem.RootElement.ValueKind == JsonValueKind.Object && problem.RootElement.TryGetProperty("code", out JsonElement code)
                && code.ValueKind == JsonValueKind.String
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
