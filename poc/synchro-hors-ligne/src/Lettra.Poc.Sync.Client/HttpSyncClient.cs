using System.Net;
using System.Net.Http.Json;
using Lettra.Poc.Sync.Contracts;

namespace Lettra.Poc.Sync.Client;

/// <summary>
/// Client HTTP de synchronisation. Le jeton de rafraîchissement est conservé dans la base locale
/// (en vrai : coffre de l'OS) ; le jeton d'accès reste en mémoire.
/// </summary>
public sealed class HttpSyncClient(HttpClient http, ClientProfile profile, LocalStore store, TimeProvider clock) : ISyncClient
{
    private const string RefreshTokenKey = "auth.refresh_token";
    private TokenResponse? _token;

    public async Task LoginAsync(string secret, CancellationToken ct)
        => await AuthenticateAsync("/api/auth/login", new LoginRequest(profile.DeviceId, secret), ct);

    public async Task EnsureAuthenticatedAsync(CancellationToken ct)
    {
        if (_token is not null && _token.ExpiresAt > clock.GetUtcNow().AddSeconds(30)) return;
        var refresh = store.GetMeta(RefreshTokenKey) ?? throw new ReauthenticationRequiredException();
        await AuthenticateAsync("/api/auth/refresh", new RefreshRequest(profile.DeviceId, refresh), ct);
    }

    private async Task AuthenticateAsync<T>(string path, T body, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(path, body, SyncHttp.Json, ct);
        await ThrowIfRefusedAsync(response, ct);
        _token = (await response.Content.ReadFromJsonAsync<TokenResponse>(SyncHttp.Json, ct))!;
        store.SetMeta(null, RefreshTokenKey, _token.RefreshToken);
    }

    public Task<PushFactsResponse> PushFactsAsync(IReadOnlyList<ValidatedInvoiceFact> facts, CancellationToken ct)
        => SendAsync<PushFactsResponse>(HttpMethod.Post, "/api/sync/facts", new PushFactsRequest(profile.DeviceId, facts), ct);

    public Task<PushIntentsResponse> PushIntentsAsync(IReadOnlyList<DraftCommand> commands, CancellationToken ct)
        => SendAsync<PushIntentsResponse>(HttpMethod.Post, "/api/sync/intents", new PushIntentsRequest(profile.DeviceId, commands), ct);

    public Task<ChangesResponse> PullSinceAsync(long syncToken, CancellationToken ct)
        => SendAsync<ChangesResponse>(HttpMethod.Get, $"/api/sync/changes?since={syncToken}", null, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new("Bearer", _token?.AccessToken);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: SyncHttp.Json);

        using var response = await http.SendAsync(request, ct);
        await ThrowIfRefusedAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<T>(SyncHttp.Json, ct))!;
    }

    private static async Task ThrowIfRefusedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new ReauthenticationRequiredException();
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            var problem = await response.Content.ReadFromJsonAsync<Problem>(SyncHttp.Json, ct);
            if (problem?.Type == SyncHttp.DeviceRevokedProblem) throw new DeviceRevokedException();
        }
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>Sonde réseau : GET /api/health doit répondre le bon service, pas une page de portail captif.</summary>
public sealed class HttpNetworkProbe(HttpClient http, Func<NetworkKind> currentKind) : INetworkProbe
{
    public async Task<NetworkStatus> ProbeAsync(CancellationToken ct)
    {
        var kind = currentKind();
        if (kind == NetworkKind.None) return new(false, kind);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var health = await http.GetFromJsonAsync<HealthResponse>("/api/health", SyncHttp.Json, timeout.Token);
            return new(health?.Service == SyncHttp.ServiceName, kind);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException)
        {
            return new(false, kind);
        }
    }
}

internal sealed record Problem(string? Type, string? Title);
