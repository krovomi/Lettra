using System.Net;
using System.Net.Http.Json;
using Lettra.Api.Endpoints;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Demo;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Serialization;
using Lettra.DemoData;
using Lettra.Domain.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Tableau de bord (vague 5, lane E) : chargement du jeu de démonstration depuis l'écran (routes <c>/api/v1/demo</c>,
/// servies par un hôte minimal tant que l'hôte de l'API ne les mappe pas) puis point des créances lu par l'API réelle
/// (mode 2, SQLite) sur ce jeu.
/// </summary>
public sealed class ReceivablesApiTests
{
    private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

    [Fact]
    public async Task Le_point_des_creances_du_jeu_de_demonstration_compte_chaque_piece_une_fois()
    {
        await using LettraApiFactory api = new();
        HttpClient client = api.ClientFor(LettraApiFactory.ManagerId);
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        IConfiguration configuration = new ConfigurationBuilder()
            .AddConfiguration(api.Services.GetRequiredService<IConfiguration>())
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Lettra:Demo:Seed"] = "true", ["Lettra:Demo:AdditionalDossiers"] = "2" })
            .Build();

        await using WebApplication demo = await DemoHostAsync(configuration, "Demo", Permission.DossierLire, Permission.FactureEmettre);
        HttpClient screen = demo.GetTestClient();

        (await screen.GetFromJsonAsync(new Uri("/api/v1/demo", UriKind.Relative), ContractsJsonContext.Default.DemoDataStatusResponse))!
            .Available.Should().BeTrue();
        using HttpResponseMessage seeded = await screen.PostAsync(new Uri("/api/v1/demo/seed", UriKind.Relative), null);
        seeded.StatusCode.Should().Be(HttpStatusCode.OK, await seeded.Content.ReadAsStringAsync());
        DemoSeedResponse result = (await seeded.Content.ReadFromJsonAsync(ContractsJsonContext.Default.DemoSeedResponse))!;
        result.Status.Should().Be(DemoSeedStatusDto.Seeded);
        result.Dossiers.Should().Be(1, "l'écran ne crée pas d'autre dossier, quoi qu'en dise la configuration");
        result.Customers.Should().BePositive();

        using HttpResponseMessage read = await client.GetAsync(new Uri("/api/v1/receivables/summary", UriKind.Relative));
        read.StatusCode.Should().Be(HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        ReceivablesSummaryResponse summary = (await read.Content.ReadFromJsonAsync(ContractsJsonContext.Default.ReceivablesSummaryResponse))!;
        List<InvoiceSummary> invoices = [];
        for (int page = 1; ; page++)
        {
            PagedResponse<InvoiceSummary> batch = (await client.GetFromJsonAsync<PagedResponse<InvoiceSummary>>(
                new Uri($"/api/v1/invoices?pageSize=200&page={page}", UriKind.Relative)))!;
            invoices.AddRange(batch.Items);
            if (batch.Items.Count == 0 || invoices.Count >= batch.TotalCount)
            {
                break;
            }
        }

        summary.Today.Should().Be(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Paris).DateTime));
        int validated = invoices.Count(i => i.Status != InvoiceStatusDto.Draft);
        (summary.NotTransmitted + summary.InProgress + summary.NeedsAttention + summary.Collected).Should().Be(validated);
        summary.Collected.Should().BePositive("le jeu déclare des encaissements complets");
        summary.OverdueCount.Should().BePositive("le jeu a des factures échues impayées")
            .And.BeLessThanOrEqualTo(invoices.Count(i => i.Kind != InvoiceKindDto.CreditNote && i.Status != InvoiceStatusDto.Draft && i.DueDate < summary.Today));
        summary.Oldest.Should().HaveCount(Math.Min(ReceivablesSummaryResponse.OldestCount, summary.OverdueCount))
            .And.BeInAscendingOrder(o => o.DueDate)
            .And.OnlyContain(o => o.DueDate < summary.Today && o.OutstandingInclTax > 0m && o.CustomerName.Length > 0);
        summary.OverdueInclTax.Should().BeGreaterThanOrEqualTo(summary.Oldest.Sum(o => o.OutstandingInclTax));

        result.Invoices.Should().Be(invoices.Count(i => i.Kind != InvoiceKindDto.CreditNote));
        using HttpResponseMessage again = await screen.PostAsync(new Uri("/api/v1/demo/seed", UriKind.Relative), null);
        (await again.Content.ReadFromJsonAsync(ContractsJsonContext.Default.DemoSeedResponse))!
            .Status.Should().Be(DemoSeedStatusDto.DossierNotEmpty, "un second clic ne double pas le jeu");
    }

    [Fact]
    public async Task Le_jeu_n_est_ni_propose_ni_charge_en_production_ni_sans_le_droit_d_emettre()
    {
        await using LettraApiFactory api = new();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddConfiguration(api.Services.GetRequiredService<IConfiguration>())
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Lettra:Demo:Seed"] = "true" })
            .Build();

        await using (WebApplication production = await DemoHostAsync(configuration, "Production", Permission.DossierLire, Permission.FactureEmettre))
        {
            HttpClient screen = production.GetTestClient();
            (await screen.GetFromJsonAsync(new Uri("/api/v1/demo", UriKind.Relative), ContractsJsonContext.Default.DemoDataStatusResponse))!
                .Available.Should().BeFalse();
            using HttpResponseMessage refused = await screen.PostAsync(new Uri("/api/v1/demo/seed", UriKind.Relative), null);
            (await refused.Content.ReadFromJsonAsync(ContractsJsonContext.Default.DemoSeedResponse))!.Status.Should().Be(DemoSeedStatusDto.RefusedInProduction);
        }

        await using WebApplication readOnly = await DemoHostAsync(configuration, "Demo", Permission.DossierLire);
        using HttpResponseMessage forbidden = await readOnly.GetTestClient().PostAsync(new Uri("/api/v1/demo/seed", UriKind.Relative), null);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Un_dossier_sans_piece_n_a_rien_en_retard_ni_a_transmettre()
    {
        await using LettraApiFactory api = new();
        HttpClient client = api.ClientFor(LettraApiFactory.ManagerId);
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());

        ReceivablesSummaryResponse summary = (await client.GetFromJsonAsync(
            new Uri("/api/v1/receivables/summary", UriKind.Relative), ContractsJsonContext.Default.ReceivablesSummaryResponse))!;

        summary.Should().BeEquivalentTo(new { OverdueCount = 0, OverdueInclTax = 0m, NotTransmitted = 0, InProgress = 0, NeedsAttention = 0, Collected = 0 });
        summary.Oldest.Should().BeEmpty();
    }

    /// <summary>Hôte minimal des routes de démonstration : le gestionnaire du dossier de test, avec les droits donnés.</summary>
    private static async Task<WebApplication> DemoHostAsync(IConfiguration configuration, string environment, params Permission[] granted)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddConfiguration(configuration);
        builder.Services.AddSingleton<ICurrentUser>(new TestUser(LettraApiFactory.ManagerId));
        builder.Services.AddSingleton<ITenantContext>(new TestTenant(new TenantDescriptor(
            new DossierId(LettraApiFactory.DossierId), new OrganisationId(LettraApiFactory.CabinetId), DatabaseProvider.Sqlite, "config:Test")));
        builder.Services.AddSingleton<IPermissionService>(new TestPermissions(granted.ToHashSet()));
        WebApplication app = builder.Build();
        app.MapGroup("/api/v1").MapDemo();
        await app.StartAsync();
        return app;
    }

    private sealed record TestUser(Guid UserId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
    }

    private sealed record TestTenant(TenantDescriptor Current) : ITenantContext
    {
        public bool IsResolved => true;
    }

    private sealed class TestPermissions(IReadOnlySet<Permission> granted) : IPermissionService
    {
        public ValueTask<IReadOnlySet<Permission>> GetEffectiveAsync(Guid userId, AuthorizationScope scope, CancellationToken cancellationToken) =>
            ValueTask.FromResult(granted);
    }
}
