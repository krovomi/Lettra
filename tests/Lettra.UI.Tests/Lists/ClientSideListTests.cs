using System.Globalization;
using Bunit;
using Lettra.Contracts.Alerts;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Identity;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages;
using Lettra.UI.Shared.Pages.Accounting;
using Lettra.UI.Shared.Pages.Cabinet;
using Lettra.UI.Tests.Administration;
using Lettra.UI.Tests.Cabinet;
using Lettra.UI.Tests.Consultation;
using Lettra.UI.Tests.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using AdminPage = Lettra.UI.Shared.Pages.Administration.Administration;

namespace Lettra.UI.Tests.Lists;

/// <summary>
/// Listes chargées en entier (alertes, Vue cabinet, Équipe et accès, soldes clients de la balance) : tri et filtres se
/// font à l'écran, avec les mêmes briques que les listes paginées.
/// </summary>
[Collection("culture")]
public sealed class ClientSideListTests : BunitContext
{
    public ClientSideListTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    [Fact]
    public void Les_alertes_se_filtrent_et_se_trient_a_l_ecran()
    {
        FakeAlertsGateway gateway = new();
        gateway.Add("integrite-chaine", AlertSeverity.S1);
        gateway.Add("integrite-numerotation", AlertSeverity.S2, AlertState.Acknowledged);
        gateway.Add("integrite-chaine", AlertSeverity.S3);
        Services.AddSingleton<IAlertsGateway>(gateway);
        IRenderedComponent<Alerts> page = Render<Alerts>();
        string[] Titles() => [.. page.FindAll("tbody tr a").Select(a => a.TextContent)];

        Titles().Should().HaveCount(3, "l'ordre du serveur est gardé tant qu'aucune colonne n'est triée");
        page.Find("#alerts-sort").GetAttribute("value").Should().BeEmpty();

        page.Find("#alerts-sort-title").Click();
        Titles().Should().Equal("Chaîne anti-fraude d'un dossier compromise", "Chaîne anti-fraude d'un dossier compromise", "Trou de numérotation dans un dossier");
        page.Find("#alerts-sort-title").ParentElement!.GetAttribute("aria-sort").Should().Be("ascending");

        page.Find("#alerts-sort").Change("Severity:Descending");
        page.FindAll("tbody .lt-severity-badge").Select(b => b.TextContent).Should().Equal("S3", "S2", "S1");

        page.Find("#alerts-search").Input("numerotation");
        Titles().Should().Equal("Trou de numérotation dans un dossier");

        page.Find("#alerts-search").Input(string.Empty);
        page.Find("#alerts-state").Change(nameof(AlertState.New));
        page.Find("#alerts-severity").Change(nameof(AlertSeverity.S2));
        page.Find("#alerts-filtered-empty").TextContent.Should().Contain("Aucun résultat");
        page.Find("#alerts-filters-toggle").TextContent.Should().Contain("2");

        page.Find("#alerts-empty-reset").Click();
        Titles().Should().HaveCount(3);

        page.Find("#alerts-sort").Change(string.Empty);
        page.Find("#alerts-sort-title").ParentElement!.GetAttribute("aria-sort").Should().Be("none");
    }

    [Fact]
    public void La_vue_cabinet_se_trie_par_dossier_ou_par_indicateur_puis_revient_a_l_urgence()
    {
        FakeCabinetGateway gateway = new();
        Services.AddCabinetFakes(gateway);
        Services.AddSingleton<IDossierSelection>(new SwitchableDossierSelection(FakeCabinetGateway.Boulangerie));
        Services.AddSingleton<ISessionGateway>(new FakeSessionGateway(new SessionResponse(true, SessionModeDto.Local, "Claire Martin", true)));
        IRenderedComponent<CabinetPage> page = Render<CabinetPage>();
        page.WaitForAssertion(() => page.FindAll(".lt-cabinet-table tbody tr").Should().HaveCount(3));
        string First() => page.FindAll(".lt-cabinet-table tbody th")[0].TextContent;

        First().Should().Contain("Garage du Centre", "le plus urgent d'abord");

        page.Find("#cabinet-sort-dossier").Click();
        First().Should().Contain("Atelier Éole");
        page.FindAll(".lt-cabinet-table tbody th")[2].TextContent.Should().Contain("Garage du Centre");

        page.Find($"#cabinet-sort-{Contracts.Cabinet.DossierIndicatorKeys.ReviewPending.Replace('.', '-')}").Click();
        First().Should().Contain("Atelier Éole", "8 points de revue, du plus grand au plus petit");

        page.Find("#cabinet-sort").Change(string.Empty);
        First().Should().Contain("Garage du Centre");
    }

    [Fact]
    public void Equipe_et_acces_trie_les_personnes_et_les_equipes()
    {
        Services.AddSingleton<IAdministrationGateway>(new FakeAdministrationGateway());
        Services.AddSingleton<IDossierSelection>(new FixedDossierSelection(FakeAdministrationGateway.Boulangerie));
        IRenderedComponent<AdminPage> page = Render<AdminPage>();
        string[] People() => [.. page.FindAll(".la-cards .la-card").Select(c => c.Id ?? string.Empty)];

        People().Should().HaveCount(6);
        People()[0].Should().Be($"person-{FakeAdministrationGateway.Me}", "Camille vient en premier de A à Z");
        People()[^1].Should().Be($"person-{FakeAdministrationGateway.Paul}");

        page.Find("#people-sort").Change("name:Descending");
        People()[0].Should().Be($"person-{FakeAdministrationGateway.Paul}");

        page.Find("#tab-teams").Click();
        string[] Teams() => [.. page.FindAll(".la-cards .la-card").Select(c => c.Id ?? string.Empty)];
        Teams().Should().Equal($"team-{FakeAdministrationGateway.TeamTpe}", $"team-{FakeAdministrationGateway.TeamSocial}");

        page.Find("#teams-sort").Change("name:Descending");
        Teams().Should().Equal($"team-{FakeAdministrationGateway.TeamSocial}", $"team-{FakeAdministrationGateway.TeamTpe}");

        page.Find("#teams-sort").Change("members:Descending");
        Teams()[0].Should().Be($"team-{FakeAdministrationGateway.TeamTpe}", "deux membres contre un");
    }

    [Fact]
    public void Les_soldes_clients_de_la_balance_se_cherchent_et_se_trient()
    {
        Guid dupont = Guid.CreateVersion7();
        Guid martin = Guid.CreateVersion7();
        FakeAccountingGateway accounting = new();
        FakeCustomersGateway customers = new();
        customers.Customers.Add(new CustomerSummary(dupont, CustomerKindDto.Company, "Dupont SARL", "732829320", "Lyon"));
        accounting.Entries.Add(FakeAccountingGateway.Invoice("F2026-0001", new DateOnly(2026, 2, 3), dupont, "Dupont SARL", 1000m, 200m));
        accounting.Entries.Add(FakeAccountingGateway.Invoice("F2026-0002", new DateOnly(2026, 10, 5), martin, "Boulangerie Martin", 250m, 50m, "707"));
        Services.AddSingleton<IAccountingGateway>(accounting);
        Services.AddSingleton<ICustomersGateway>(customers);
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero)));
        Services.GetRequiredService<NavigationManager>().NavigateTo("accounting/balance?from=2026-01-01&to=2026-12-31");
        IRenderedComponent<BalancePage> page = Render<BalancePage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        string[] Customers() => [.. page.FindAll("#balance-customers-table tbody tr td:first-child").Select(c => c.TextContent.Trim())];

        Customers().Should().Equal("Dupont SARL", "Boulangerie Martin");
        page.Find("#balance-sort-balance").ParentElement!.GetAttribute("aria-sort").Should().Be("descending");

        page.Find("#balance-sort-customer").Click();
        Customers().Should().Equal("Boulangerie Martin", "Dupont SARL");

        page.Find("#balance-customers-search").Input("dupont");
        Customers().Should().Equal("Dupont SARL");
        page.Find("#balance-customers-filters-reset").Click();
        Customers().Should().HaveCount(2);
    }
}
