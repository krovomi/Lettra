using System.Buffers.Text;
using System.Net;
using System.Text;
using System.Text.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Identity;
using Lettra.UI.Gateway.Http.Device;

namespace Lettra.Api.Tests;

/// <summary>
/// Connexion de l'application de bureau (client public <c>lettra-maui</c>, annexe F §2) : code + PKCE S256, state et
/// nonce, rotation du jeton de rafraîchissement tolérante à une réponse perdue (ADR-0010).
/// </summary>
public sealed class DeviceSignInTests : IDisposable
{
    private const string Authority = "https://auth.test/realms/lettra";
    private static readonly Guid User = Guid.CreateVersion7();

    private readonly FakeKeycloak _keycloak = new();
    private readonly InMemoryTokenVault _vault = new();
    private readonly FakeBrowser _browser = new();
    private readonly Clock _clock = new(new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero));
    private readonly HttpClient _http;
    private readonly OidcPkceClient _client;

    public DeviceSignInTests()
    {
        _http = new HttpClient(_keycloak);
        _client = new OidcPkceClient(_http, new DeviceSignInOptions { Authority = new Uri(Authority), BaseAddress = new Uri("https://api.test") }, _vault, _browser, _clock);
        _keycloak.Clock = _clock;
        _browser.Keycloak = _keycloak;
    }

    public void Dispose()
    {
        _client.Dispose();
        _http.Dispose();
        _keycloak.Dispose();
    }

    [Fact]
    public async Task La_connexion_passe_par_le_navigateur_avec_PKCE_et_garde_les_jetons()
    {
        OperationResult<DeviceTokens> signedIn = await _client.SignInAsync(CancellationToken.None);

        signedIn.IsSuccess.Should().BeTrue();
        Dictionary<string, string> authorize = Query(_browser.Opened!);
        authorize["response_type"].Should().Be("code");
        authorize["client_id"].Should().Be("lettra-maui");
        authorize["code_challenge_method"].Should().Be("S256");
        authorize["scope"].Should().Contain("offline_access");
        authorize["redirect_uri"].Should().Be(FakeBrowser.Redirect.ToString());
        authorize["code_challenge"].Should().Be(Challenge(_keycloak.LastForm!["code_verifier"]), "le vérificateur envoyé au jeton correspond au défi");
        _keycloak.LastForm!["grant_type"].Should().Be("authorization_code");

        DeviceTokens stored = (await _vault.ReadAsync(CancellationToken.None))!;
        stored.UserId.Should().Be(User);
        stored.DisplayName.Should().Be("Camille Martin");
        stored.RefreshToken.Should().Be("refresh-1");
        stored.ToString().Should().NotContain("access", "aucun jeton dans les traces");
    }

    [Fact]
    public async Task Un_state_qui_ne_correspond_pas_est_refuse()
    {
        _browser.TamperState = true;

        OperationResult<DeviceTokens> signedIn = await _client.SignInAsync(CancellationToken.None);

        signedIn.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Security.Unauthenticated);
        _keycloak.TokenRequests.Should().Be(0);
        (await _vault.ReadAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Un_jeton_d_identite_au_mauvais_nonce_est_refuse()
    {
        _keycloak.WrongNonce = true;

        (await _client.SignInAsync(CancellationToken.None)).IsSuccess.Should().BeFalse();
        (await _vault.ReadAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task L_abandon_dans_le_navigateur_ne_connecte_pas()
    {
        _browser.Abandon = true;

        (await _client.SignInAsync(CancellationToken.None)).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Le_jeton_d_acces_est_renouvele_avant_son_echeance_et_le_jeton_de_rafraichissement_tourne()
    {
        await _client.SignInAsync(CancellationToken.None);
        (await _client.GetAccessTokenAsync(CancellationToken.None)).Should().Be("access-1");

        _clock.Advance(TimeSpan.FromMinutes(4.5));
        (await _client.GetAccessTokenAsync(CancellationToken.None)).Should().Be("access-2");

        _keycloak.LastForm!["grant_type"].Should().Be("refresh_token");
        _keycloak.LastForm["refresh_token"].Should().Be("refresh-1");
        (await _vault.ReadAsync(CancellationToken.None))!.RefreshToken.Should().Be("refresh-2");
    }

    [Fact]
    public async Task Une_reponse_de_rafraichissement_perdue_garde_l_ancien_jeton_pour_reessayer()
    {
        await _client.SignInAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(6));
        _keycloak.LoseNextTokenResponse = true;

        (await _client.GetAccessTokenAsync(CancellationToken.None)).Should().BeNull("le jeton d'accès a échu et le serveur n'a pas répondu");
        (await _vault.ReadAsync(CancellationToken.None))!.RefreshToken.Should().Be("refresh-1");

        (await _client.GetAccessTokenAsync(CancellationToken.None)).Should().Be("access-3");
        _keycloak.LastForm!["refresh_token"].Should().Be("refresh-1", "Keycloak accepte l'ancien jeton une fois (refreshTokenMaxReuse = 1)");
    }

    [Fact]
    public async Task Un_jeton_de_rafraichissement_refuse_termine_la_session()
    {
        await _client.SignInAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(6));
        _keycloak.RejectRefresh = true;

        (await _client.GetAccessTokenAsync(CancellationToken.None)).Should().BeNull();
        (await _vault.ReadAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Des_appels_simultanes_ne_rafraichissent_qu_une_fois()
    {
        await _client.SignInAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(6));
        int before = _keycloak.TokenRequests;

        string?[] tokens = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _client.GetAccessTokenAsync(CancellationToken.None)));

        tokens.Should().OnlyContain(t => t == "access-2");
        (_keycloak.TokenRequests - before).Should().Be(1);
    }

    [Fact]
    public async Task La_deconnexion_revoque_le_jeton_et_vide_le_coffre()
    {
        await _client.SignInAsync(CancellationToken.None);
        DeviceSessionGateway sessions = new(_client, _vault);
        (await sessions.GetAsync(CancellationToken.None)).Value!.Should().BeEquivalentTo(
            new { IsAuthenticated = true, Mode = SessionModeDto.Oidc, DisplayName = "Camille Martin", SignOutUrl = DeviceSessionGateway.SignOutRoute });

        await sessions.SignOutAsync(CancellationToken.None);

        _keycloak.Revoked.Should().Equal("refresh-1");
        (await sessions.GetAsync(CancellationToken.None)).Value!.IsAuthenticated.Should().BeFalse();
        sessions.SignInPath.Should().Be(DeviceSessionGateway.SignInRoute);
    }

    [Fact]
    public async Task Le_retour_par_l_adresse_de_bouclage_rend_le_code_au_poste()
    {
        using HttpClient browser = new(new HttpClientHandler { UseProxy = false });
        using LoopbackAuthorizationBrowser loopback = new(async authorize =>
        {
            // Le navigateur demande d'abord son icône, puis suit la redirection de Keycloak.
            Uri redirect = new(_loopbackRedirect!.GetLeftPart(UriPartial.Authority));
            using HttpResponseMessage icon = await browser.GetAsync(new Uri(redirect, "/favicon.ico"));
            using HttpResponseMessage back = await browser.GetAsync(new Uri(_loopbackRedirect + "?code=abc&state=xyz"));
        });
        _loopbackRedirect = loopback.PrepareRedirectUri();
        _loopbackRedirect.Host.Should().Be("127.0.0.1");

        Uri? callback = await loopback.AuthorizeAsync(new Uri("https://auth.test/authorize"), _loopbackRedirect, CancellationToken.None);

        callback.Should().NotBeNull();
        callback!.AbsolutePath.Should().Be("/callback");
        callback.Query.Should().Be("?code=abc&state=xyz");
    }

    private Uri? _loopbackRedirect;

    private static string Challenge(string verifier) =>
        Base64Url.EncodeToString(System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static Dictionary<string, string> Query(Uri uri) => uri.Query.TrimStart('?').Split('&')
        .Select(p => p.Split('=', 2))
        .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delay) => _now += delay;
    }

    private sealed class FakeBrowser : IAuthorizationBrowser
    {
        public static readonly Uri Redirect = new("http://127.0.0.1:50123/callback");

        public FakeKeycloak Keycloak { get; set; } = null!;

        public Uri? Opened { get; private set; }

        public bool TamperState { get; set; }

        public bool Abandon { get; set; }

        public Uri PrepareRedirectUri() => Redirect;

        public Task<Uri?> AuthorizeAsync(Uri authorizeUrl, Uri redirectUri, CancellationToken cancellationToken)
        {
            Opened = authorizeUrl;
            if (Abandon)
            {
                return Task.FromResult<Uri?>(null);
            }

            Dictionary<string, string> query = Query(authorizeUrl);
            Keycloak.Nonce = query["nonce"];
            string state = TamperState ? "autre" : query["state"];
            return Task.FromResult<Uri?>(new Uri($"{redirectUri}?state={Uri.EscapeDataString(state)}&code=code-1&session_state=s"));
        }
    }

    private sealed class FakeKeycloak : HttpMessageHandler
    {
        private int _issued;

        public TimeProvider Clock { get; set; } = TimeProvider.System;

        public string? Nonce { get; set; }

        public bool WrongNonce { get; set; }

        public bool LoseNextTokenResponse { get; set; }

        public bool RejectRefresh { get; set; }

        public int TokenRequests { get; private set; }

        public Dictionary<string, string>? LastForm { get; private set; }

        public List<string> Revoked { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal))
            {
                return Json(new Dictionary<string, string>
                {
                    ["issuer"] = Authority,
                    ["authorization_endpoint"] = Authority + "/protocol/openid-connect/auth",
                    ["token_endpoint"] = Authority + "/protocol/openid-connect/token",
                    ["revocation_endpoint"] = Authority + "/protocol/openid-connect/revoke",
                });
            }

            Dictionary<string, string> form = (await request.Content!.ReadAsStringAsync(cancellationToken)).Split('&')
                .Select(p => p.Split('=', 2))
                .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
            if (path.EndsWith("/revoke", StringComparison.Ordinal))
            {
                Revoked.Add(form["token"]);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            lock (this)
            {
                TokenRequests++;
                LastForm = form;
            }

            if (form["grant_type"] == "refresh_token" && RejectRefresh)
            {
                return Json(new Dictionary<string, string> { ["error"] = "invalid_grant" }, HttpStatusCode.BadRequest);
            }

            int n = Interlocked.Increment(ref _issued);
            if (LoseNextTokenResponse)
            {
                LoseNextTokenResponse = false;
                throw new HttpRequestException("réponse perdue");
            }

            Dictionary<string, object> body = new()
            {
                ["access_token"] = $"access-{n}",
                ["expires_in"] = 300,
                ["refresh_token"] = $"refresh-{n}",
                ["token_type"] = "Bearer",
            };
            if (form["grant_type"] == "authorization_code")
            {
                body["id_token"] = IdToken();
            }

            return Json(body);
        }

        private string IdToken()
        {
            string Part(object value) => Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(value));
            return Part(new { alg = "RS256", typ = "JWT" }) + "." + Part(new Dictionary<string, object>
            {
                ["iss"] = Authority,
                ["aud"] = "lettra-maui",
                ["sub"] = User.ToString(),
                ["nonce"] = WrongNonce ? "autre" : Nonce!,
                ["exp"] = Clock.GetUtcNow().AddMinutes(5).ToUnixTimeSeconds(),
                ["name"] = "Camille Martin",
            }) + ".signature";
        }

        private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}
