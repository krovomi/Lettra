using System.Text;
using System.Text.Json;
using Lettra.Domain.Common;
using Lettra.Supervision.Application;
using Lettra.Supervision.Domain;
using Lettra.Supervision.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Supervision.Tests;

/// <summary>
/// Le webhook Alertmanager de bout en bout, par le délégué de la route réellement enregistrée (sans serveur HTTP) :
/// jeton, charge utile, comptage des alertes reçues et refusées ; puis la traduction des étiquettes en signal.
/// </summary>
public sealed class WebhookTests : IAsyncDisposable
{
    private const string Token = "s3cr3t-cellule";
    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    private readonly List<WebApplication> _apps = [];

    public async ValueTask DisposeAsync()
    {
        foreach (WebApplication app in _apps)
        {
            await app.DisposeAsync();
        }
    }

    private WebApplication App(string? token)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Configuration["Lettra:Supervision:WebhookToken"] = token;
        builder.Services.AddSupervision(builder.Configuration);
        builder.Services.AddFakeDirectory(new FakeDirectory());
        WebApplication app = builder.Build();
        _apps.Add(app);
        app.MapSupervisionWebhooks();
        return app;
    }

    private static IReadOnlyList<RouteEndpoint> Endpoints(WebApplication app) =>
        [.. ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()];

    private static async Task<(int Status, string Body)> PostAsync(WebApplication app, string? authorization, string body)
    {
        RouteEndpoint endpoint = Endpoints(app).Single();
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        DefaultHttpContext context = new() { RequestServices = scope.ServiceProvider };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = SupervisionEndpoints.AlertmanagerRoute;
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        MemoryStream response = new();
        context.Response.Body = response;
        await endpoint.RequestDelegate!(context);
        return (context.Response.StatusCode, Encoding.UTF8.GetString(response.ToArray()));
    }

    private static string Alert(string status, string labels, string? annotations = null) => $$"""
        { "status": "{{status}}", "labels": {{labels}}, "annotations": {{annotations ?? "{}"}}, "startsAt": "2026-10-14T08:00:00Z", "endsAt": "2026-10-14T08:05:00Z" }
        """;

    private const string ApiDownLabels = """{ "alertname": "LettraApiIndisponible", "severity": "S1", "category": "availability", "scope": "cell", "cell": "mut-fr-01" }""";

    [Fact]
    public void Sans_jeton_configure_aucun_webhook_n_est_expose()
    {
        Endpoints(App(null)).Should().BeEmpty();
        Endpoints(App("   ")).Should().BeEmpty();
    }

    [Fact]
    public void Avec_un_jeton_le_webhook_ecoute_en_POST_sur_la_route_interne()
    {
        RouteEndpoint endpoint = Endpoints(App(Token)).Should().ContainSingle().Subject;

        endpoint.RoutePattern.RawText.Should().Be(SupervisionEndpoints.AlertmanagerRoute);
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Should().Equal(HttpMethods.Post);
    }

    [Fact]
    public void Enregistrement_sans_routeur_refuse()
    {
        Action map = () => SupervisionEndpoints.MapSupervisionWebhooks(null!);
        map.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer mauvais")]
    [InlineData("Basic czNjcjN0LWNlbGx1bGU=")]
    [InlineData(Token)]
    public async Task Un_appel_sans_le_bon_jeton_est_refuse_sans_lire_la_charge(string? authorization)
    {
        WebApplication app = App(Token);

        (int status, _) = await PostAsync(app, authorization, "pas du json");

        status.Should().Be(StatusCodes.Status401Unauthorized);
        (await app.Services.GetRequiredService<IAlertStore>().ListActiveAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{ "version": "4", "status": "firing" }""")]
    public async Task Une_charge_sans_liste_d_alertes_est_une_mauvaise_requete(string body)
    {
        (int status, _) = await PostAsync(App(Token), "Bearer " + Token, body);

        status.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task Une_charge_illisible_est_une_mauvaise_requete_pour_ne_pas_etre_rejouee()
    {
        // Alertmanager ne rejoue que les 5xx : une charge illisible doit recevoir 400, sinon elle reviendrait sans fin.
        (int status, _) = await PostAsync(App(Token), "Bearer " + Token, "{ pas du json");

        status.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task Les_alertes_recues_et_refusees_sont_comptees_sans_faire_rejouer_le_lot()
    {
        WebApplication app = App(Token);
        string body = $$"""
            { "version": "4", "status": "firing", "alerts": [
              {{Alert("firing", ApiDownLabels, """{ "title": "API Lettra indisponible" }""")}},
              {{Alert("firing", """{ "alertname": "SansGravite", "category": "availability", "scope": "platform" }""")}},
              {{Alert("resolved", """{ "alertname": "JamaisLevee", "severity": "S3", "category": "performance", "scope": "platform" }""")}}
            ] }
            """;

        (int status, string response) = await PostAsync(app, "Bearer " + Token, body);

        status.Should().Be(StatusCodes.Status200OK);
        using JsonDocument receipt = JsonDocument.Parse(response);
        receipt.RootElement.GetProperty("accepted").GetInt32().Should().Be(1);
        receipt.RootElement.GetProperty("rejected").GetInt32().Should().Be(2, "une alerte mal étiquetée, une résolution d'alerte inconnue");
        IReadOnlyList<Alert> alerts = await app.Services.GetRequiredService<IAlertStore>().ListActiveAsync(CancellationToken.None);
        alerts.Should().ContainSingle().Which.Title.Should().Be("API Lettra indisponible");
    }

    [Fact]
    public async Task Une_liste_vide_est_acceptee_sans_rien_compter()
    {
        (int status, string response) = await PostAsync(App(Token), "Bearer " + Token, """{ "alerts": [] }""");

        status.Should().Be(StatusCodes.Status200OK);
        using JsonDocument receipt = JsonDocument.Parse(response);
        receipt.RootElement.GetProperty("accepted").GetInt32().Should().Be(0);
        receipt.RootElement.GetProperty("rejected").GetInt32().Should().Be(0);
    }

    [Fact]
    public void Une_alerte_resolue_date_de_sa_fin_ou_de_son_debut_si_la_fin_manque()
    {
        Dictionary<string, string> labels = new() { ["alertname"] = "R", ["severity"] = "s2", ["category"] = "security", ["scope"] = "platform" };

        Result<AlertSignal> resolved = AlertmanagerMapper.ToSignal(new AlertmanagerAlert("RESOLVED", labels, null, T0, T0.AddMinutes(5)));
        resolved.Value.Should().BeEquivalentTo(new { RuleId = "R", Title = "R", Severity = Severity.S2, Category = AlertCategory.Security, Firing = false, At = T0.AddMinutes(5) });
        resolved.Value.Scope.Should().Be(AlertScope.Platform());

        AlertmanagerMapper.ToSignal(new AlertmanagerAlert("resolved", labels, null, T0, default)).Value.At.Should().Be(T0);
        AlertmanagerMapper.ToSignal(new AlertmanagerAlert("firing", labels, null, T0, T0.AddMinutes(5))).Value.Should().Match<AlertSignal>(s => s.Firing && s.At == T0);
        AlertmanagerMapper.ToSignal(new AlertmanagerAlert(null, labels, null, T0, default)).Value.Firing.Should().BeTrue("sans statut, l'alerte est en cours");
    }

    [Theory]
    [InlineData("performance", AlertCategory.Performance)]
    [InlineData("compliance", AlertCategory.Compliance)]
    [InlineData("functional", AlertCategory.Functional)]
    [InlineData("cost", AlertCategory.Cost)]
    [InlineData("availability", AlertCategory.Availability)]
    [InlineData("security", AlertCategory.Security)]
    public void Chaque_categorie_des_regles_de_la_cellule_est_reconnue(string label, AlertCategory expected)
    {
        Dictionary<string, string> labels = new() { ["alertname"] = "R", ["severity"] = "S4", ["category"] = label, ["scope"] = "platform" };

        AlertmanagerMapper.ToSignal(new AlertmanagerAlert("firing", labels, null, T0, default)).Value.Category.Should().Be(expected);
    }

    [Theory]
    [InlineData("severity", "S9")]
    [InlineData("severity", "7")]
    [InlineData("severity", "")]
    [InlineData("category", "Availability")]
    [InlineData("category", "disque")]
    [InlineData("alertname", null)]
    public void Une_etiquette_manquante_ou_inconnue_n_est_pas_devinee(string label, string? value)
    {
        Dictionary<string, string> labels = new() { ["alertname"] = "R", ["severity"] = "S1", ["category"] = "availability", ["scope"] = "platform" };
        if (value is null)
        {
            labels.Remove(label);
        }
        else
        {
            labels[label] = value;
        }

        AlertmanagerMapper.ToSignal(new AlertmanagerAlert("firing", labels, null, T0, default)).Errors.Select(e => e.Code)
            .Should().Equal(AlertmanagerMapper.UnmappedCode);
    }

    [Fact]
    public void Une_gravite_numerique_definie_est_acceptee_comme_son_nom()
    {
        // Enum.TryParse lit aussi les nombres : « 1 » vaut S1. Comportement actuel, épinglé.
        Dictionary<string, string> labels = new() { ["alertname"] = "R", ["severity"] = "1", ["category"] = "cost", ["scope"] = "platform" };

        AlertmanagerMapper.ToSignal(new AlertmanagerAlert("firing", labels, null, T0, default)).Value.Severity.Should().Be(Severity.S1);
    }

    [Fact]
    public void Une_alerte_sans_etiquettes_est_refusee()
    {
        AlertmanagerMapper.ToSignal(new AlertmanagerAlert("firing", null, null, T0, default)).Errors.Select(e => e.Code)
            .Should().Equal(AlertmanagerMapper.UnmappedCode);
        Action withoutAlert = () => AlertmanagerMapper.ToSignal(null!);
        withoutAlert.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Portees_cabinet_et_dossier_lues_des_etiquettes()
    {
        Guid cabinet = Guid.CreateVersion7();
        Guid dossier = Guid.CreateVersion7();
        Dictionary<string, string> labels = new()
        {
            ["alertname"] = "R",
            ["severity"] = "S3",
            ["category"] = "compliance",
            ["cell"] = "mut-fr-01",
            ["cabinet"] = cabinet.ToString(),
            ["dossier"] = dossier.ToString(),
        };

        labels["scope"] = "cabinet";
        AlertmanagerMapper.ToSignal(new AlertmanagerAlert("firing", labels, null, T0, default)).Value.Scope
            .Should().Be(AlertScope.ForCabinet("mut-fr-01", cabinet).Value);
        labels["scope"] = "dossier";
        AlertmanagerMapper.ToSignal(new AlertmanagerAlert("firing", labels, null, T0, default)).Value.Scope
            .Should().Be(AlertScope.ForDossier("mut-fr-01", cabinet, dossier).Value);
    }

    [Theory]
    [InlineData("cell", null, null, null)]
    [InlineData("cabinet", "mut-fr-01", "pas-un-guid", null)]
    [InlineData("dossier", "mut-fr-01", "4d1e0f7a-1b2c-4d5e-8f90-123456789abc", null)]
    [InlineData("groupe", "mut-fr-01", null, null)]
    [InlineData(null, "mut-fr-01", null, null)]
    public void Une_portee_incomplete_ou_inconnue_est_refusee(string? scope, string? cell, string? cabinet, string? dossier)
    {
        Dictionary<string, string> labels = new() { ["alertname"] = "R", ["severity"] = "S3", ["category"] = "cost" };
        foreach ((string key, string? value) in new[] { ("scope", scope), ("cell", cell), ("cabinet", cabinet), ("dossier", dossier) })
        {
            if (value is not null)
            {
                labels[key] = value;
            }
        }

        AlertmanagerMapper.ToSignal(new AlertmanagerAlert("firing", labels, null, T0, default)).Errors.Select(e => e.Code)
            .Should().Equal(SupervisionErrors.ScopeIncompleteCode);
    }
}
