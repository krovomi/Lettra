using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.Contracts.Sync;

namespace Lettra.Sync.Client;

/// <summary>
/// Transport HTTP vers l'API (<c>/api/v1/sync/…</c>). Le <see cref="HttpClient"/> est fourni par l'hôte, avec son adresse
/// et son jeton (connexion PKCE du poste) ; le dossier part dans l'en-tête <c>X-Dossier</c> comme pour toute l'API.
/// </summary>
public sealed class HttpSyncTransport(HttpClient http) : ISyncTransport
{
    public async Task<PushSyncIntentsResponse> PushAsync(Guid dossierId, PushSyncIntentsRequest request, CancellationToken cancellationToken)
    {
        using HttpRequestMessage message = new(HttpMethod.Post, new Uri(SyncHttp.IntentsPath, UriKind.Relative))
        {
            Content = JsonContent.Create(request, ContractsJsonContext.Default.PushSyncIntentsRequest),
        };
        message.Headers.Add(LettraHeaders.Dossier, dossierId.ToString());
        using HttpResponseMessage response = await http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        Ensure(response);
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
        message.Headers.Add(LettraHeaders.Dossier, dossierId.ToString());
        using HttpResponseMessage response = await http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        Ensure(response);
        return (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.SyncChangesResponse, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<OpenDeviceSeriesResponse> OpenDeviceSeriesAsync(Guid dossierId, OpenDeviceSeriesRequest request, CancellationToken cancellationToken)
    {
        using HttpRequestMessage message = new(HttpMethod.Post, new Uri(SyncHttp.DeviceSeriesPath, UriKind.Relative))
        {
            Content = JsonContent.Create(request, ContractsJsonContext.Default.OpenDeviceSeriesRequest),
        };
        message.Headers.Add(LettraHeaders.Dossier, dossierId.ToString());
        using HttpResponseMessage response = await http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        Ensure(response);
        return (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.OpenDeviceSeriesResponse, cancellationToken).ConfigureAwait(false))!;
    }

    private static void Ensure(HttpResponseMessage response)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
                throw new SyncRefusedException(SyncFailure.Unauthenticated);
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
}
