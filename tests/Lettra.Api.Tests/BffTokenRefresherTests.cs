using System.Globalization;
using System.Security.Claims;
using Lettra.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lettra.Api.Tests;

/// <summary>
/// Rafraîchissement des jetons de la session du BFF : un seul appel à Keycloak pour des requêtes simultanées
/// (la rotation prendrait le second pour une réutilisation), fermeture de session sur révocation, maintien de
/// la session si Keycloak est injoignable.
/// </summary>
public sealed class BffTokenRefresherTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ScriptedTokenEndpoint(TokenRefreshResult result) : ITokenEndpointClient
    {
        private int _calls;

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls => _calls;

        public async Task<TokenRefreshResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            await Release.Task;
            return result;
        }
    }

    private sealed class RecordingAuthentication : IAuthenticationService
    {
        public List<string?> SignedOut { get; } = [];

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => throw new NotSupportedException();

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => throw new NotSupportedException();

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            SignedOut.Add(scheme);
            return Task.CompletedTask;
        }
    }

    private readonly RecordingAuthentication _authentication = new();

    private static BffTokenRefresher Refresher(ITokenEndpointClient client) =>
        new(client, new FixedClock(), NullLogger<BffTokenRefresher>.Instance);

    private CookieValidatePrincipalContext Context(string refreshToken, TimeSpan expiresIn)
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService>(_authentication).BuildServiceProvider(),
        };
        var properties = new AuthenticationProperties();
        properties.StoreTokens(
        [
            new AuthenticationToken { Name = "access_token", Value = "ancien-acces" },
            new AuthenticationToken { Name = "refresh_token", Value = refreshToken },
            new AuthenticationToken { Name = "expires_at", Value = Now.Add(expiresIn).ToString("o", CultureInfo.InvariantCulture) },
        ]);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.CreateVersion7().ToString())], "test"));
        return new CookieValidatePrincipalContext(
            http,
            new AuthenticationScheme(LettraAuthSchemes.Cookie, null, typeof(CookieAuthenticationHandler)),
            new CookieAuthenticationOptions(),
            new AuthenticationTicket(principal, properties, LettraAuthSchemes.Cookie));
    }

    [Fact]
    public async Task Un_jeton_encore_valide_n_est_pas_rafraichi()
    {
        var endpoint = new ScriptedTokenEndpoint(new TokenRefreshResult(TokenRefreshOutcome.Refreshed, "nouveau", "r2", Now.AddMinutes(5)));
        CookieValidatePrincipalContext context = Context("r1", TimeSpan.FromMinutes(4));

        await Refresher(endpoint).ValidateAsync(context);

        endpoint.Calls.Should().Be(0);
        context.ShouldRenew.Should().BeFalse();
    }

    [Fact]
    public async Task Des_requetes_simultanees_partagent_un_seul_rafraichissement()
    {
        var endpoint = new ScriptedTokenEndpoint(new TokenRefreshResult(TokenRefreshOutcome.Refreshed, "nouveau", "r2", Now.AddMinutes(5)));
        BffTokenRefresher refresher = Refresher(endpoint);
        CookieValidatePrincipalContext[] contexts = [.. Enumerable.Range(0, 5).Select(_ => Context("r1", TimeSpan.FromSeconds(30)))];

        Task[] requests = [.. contexts.Select(refresher.ValidateAsync)];
        endpoint.Release.SetResult();
        await Task.WhenAll(requests);

        endpoint.Calls.Should().Be(1);
        contexts.Should().AllSatisfy(c =>
        {
            c.ShouldRenew.Should().BeTrue();
            c.Properties.GetTokenValue("access_token").Should().Be("nouveau");
            c.Properties.GetTokenValue("refresh_token").Should().Be("r2");
        });
    }

    [Fact]
    public async Task Un_rafraichissement_refuse_ferme_la_session()
    {
        var endpoint = new ScriptedTokenEndpoint(new TokenRefreshResult(TokenRefreshOutcome.Revoked));
        endpoint.Release.SetResult();
        CookieValidatePrincipalContext context = Context("r1", TimeSpan.FromSeconds(10));

        await Refresher(endpoint).ValidateAsync(context);

        context.Principal.Should().BeNull();
        _authentication.SignedOut.Should().Equal(LettraAuthSchemes.Cookie);
    }

    [Fact]
    public async Task Keycloak_injoignable_laisse_la_session_ouverte()
    {
        var endpoint = new ScriptedTokenEndpoint(new TokenRefreshResult(TokenRefreshOutcome.Unavailable));
        endpoint.Release.SetResult();
        CookieValidatePrincipalContext context = Context("r1", TimeSpan.FromSeconds(10));

        await Refresher(endpoint).ValidateAsync(context);

        context.Principal.Should().NotBeNull();
        context.ShouldRenew.Should().BeFalse();
        _authentication.SignedOut.Should().BeEmpty();
        context.Properties.GetTokenValue("refresh_token").Should().Be("r1");
    }
}
