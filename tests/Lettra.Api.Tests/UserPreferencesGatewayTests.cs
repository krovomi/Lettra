using System.Net;
using System.Text;
using Lettra.Contracts.Common;
using Lettra.Contracts.Preferences;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Les préférences du compte, par HTTP et en direct. Un serveur sans la route (plus ancien) ou un poste isolé sans base
/// Catalog rendent <c>Preferences.Unavailable</c> : l'écran garde alors les préférences sur l'appareil.
/// </summary>
public sealed class UserPreferencesGatewayTests : IClassFixture<LettraApiFactory>, IDisposable
{
    private readonly GatewayHarness _harness;

    public UserPreferencesGatewayTests(LettraApiFactory factory) => _harness = new GatewayHarness(factory);

    public void Dispose() => _harness.Dispose();

    private static async Task<List<IReadOnlyList<ErrorDetail>>> CallAllAsync(IUserPreferencesGateway gateway)
    {
        Guid dossier = Guid.CreateVersion7();
        return
        [
            (await gateway.GetAsync(default)).Errors,
            (await gateway.SaveAsync(new SaveUserPreferencesRequest(), default)).Errors,
            (await gateway.RecordUsageAsync(new RecordUsageRequest(dossier, UsageKindDto.Customer, Guid.CreateVersion7()), default)).Errors,
            (await gateway.ForgetUsageAsync(dossier, default)).Errors,
            (await gateway.ForgetUsageAsync(null, default)).Errors,
            (await gateway.ResetAsync(default)).Errors,
        ];
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body, string media = "application/json") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, media) };

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "application/problem+json")]
    [InlineData(HttpStatusCode.MethodNotAllowed, "application/problem+json")]
    [InlineData(HttpStatusCode.OK, "text/html")]
    public async Task Un_serveur_sans_la_route_rend_les_preferences_indisponibles(HttpStatusCode status, string media)
    {
        List<HttpRequestMessage> requests = [];
        await using ServiceProvider services = GatewayHarness.Answering(request =>
        {
            requests.Add(request);
            return Json(status, media == "text/html" ? "<!doctype html><html></html>" : "{}", media);
        });

        List<IReadOnlyList<ErrorDetail>> failures = await CallAllAsync(services.GetRequiredService<IUserPreferencesGateway>());

        failures.Should().AllSatisfy(errors => errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Preferences.Unavailable));
        requests.Select(r => $"{r.Method} {r.RequestUri!.PathAndQuery}").Should().HaveCount(6)
            .And.Contain("GET /api/v1/me/preferences", "PUT /api/v1/me/preferences", "POST /api/v1/me/preferences/usage", "DELETE /api/v1/me/preferences/usage", "DELETE /api/v1/me/preferences")
            .And.Contain(p => p.StartsWith("DELETE /api/v1/me/preferences/usage?dossierId=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Une_reponse_illisible_rend_les_preferences_indisponibles()
    {
        await using ServiceProvider services = GatewayHarness.Answering(_ => Json(HttpStatusCode.OK, "{ pas du json"));

        (await services.GetRequiredService<IUserPreferencesGateway>().GetAsync(default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Preferences.Unavailable);
    }

    [Fact]
    public async Task Le_compte_est_lu_et_les_refus_sont_rendus_avec_leur_code()
    {
        string body = """
            {"workspace":{"theme":"Dark","language":"en-US","navFavorites":["billing/quotes"],"collapsedSections":[],"favoriteDossiers":[],"recentDossiers":[],"rankByUsage":false},
             "entryDefaults":[],"usage":[],"version":3,"updatedAt":"2026-10-19T09:00:00+00:00"}
            """;
        await using ServiceProvider services = GatewayHarness.Answering(request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, body)
            : Json(HttpStatusCode.PreconditionFailed, """{"code":"Concurrency.VersionMismatch"}""", "application/problem+json"));
        IUserPreferencesGateway gateway = services.GetRequiredService<IUserPreferencesGateway>();

        OperationResult<UserPreferencesResponse> read = await gateway.GetAsync(default);
        read.IsSuccess.Should().BeTrue();
        read.Value!.Version.Should().Be(3);
        read.Value.Workspace.Theme.Should().Be("Dark");
        read.Value.Workspace.RankByUsage.Should().BeFalse();

        OperationResult<UserPreferencesResponse> stale = await gateway.SaveAsync(new SaveUserPreferencesRequest { Version = 2 }, default);
        stale.IsStale.Should().BeTrue("un autre appareil a écrit : l'écran relit puis refait le changement");
    }

    [Fact]
    public async Task Serveur_injoignable_rend_Api_Unreachable()
    {
        await using ServiceProvider services = GatewayHarness.Unreachable();

        (await CallAllAsync(services.GetRequiredService<IUserPreferencesGateway>()))
            .Should().AllSatisfy(errors => errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unreachable));
    }

    [Fact]
    public async Task Le_poste_isole_garde_les_preferences_sur_l_appareil()
    {
        await using ServiceProvider services = _harness.InProcessGateway(null);

        (await CallAllAsync(services.GetRequiredService<IUserPreferencesGateway>()))
            .Should().AllSatisfy(errors => errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Preferences.Unavailable));
    }
}
