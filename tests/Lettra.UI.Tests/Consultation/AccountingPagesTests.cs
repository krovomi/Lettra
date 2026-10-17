using System.Globalization;
using Bunit;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Explorer;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Accounting;
using Lettra.UI.Tests.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Consultation;

/// <summary>Consultation de la comptabilité : journal des ventes, balance, grand livre d'un compte.</summary>
[Collection("culture")]
public sealed class AccountingPagesTests : BunitContext
{
    private static readonly Guid Dupont = Guid.CreateVersion7();
    private static readonly Guid Martin = Guid.CreateVersion7();

    private readonly FakeAccountingGateway _accounting = new();
    private readonly FakeCustomersGateway _customers = new();

    public AccountingPagesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        ExplorerSettings.SearchDelay = TimeSpan.FromMilliseconds(1);
        Services.AddLettraUi();
        Services.AddSingleton<IAccountingGateway>(_accounting);
        Services.AddSingleton<ICustomersGateway>(_customers);
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
        _customers.Customers.Add(new CustomerSummary(Dupont, CustomerKindDto.Company, "Dupont SARL", "732829320", "Lyon"));
        _accounting.Entries.Add(FakeAccountingGateway.Invoice("F2025-0099", new DateOnly(2025, 12, 20), Dupont, "Dupont SARL", 500m, 100m));
        _accounting.Entries.Add(FakeAccountingGateway.Invoice("F2026-0001", new DateOnly(2026, 2, 3), Dupont, "Dupont SARL", 1000m, 200m));
        _accounting.Entries.Add(FakeAccountingGateway.Invoice("F2026-0002", new DateOnly(2026, 10, 5), Martin, "Boulangerie Martin", 250m, 50m, "707"));
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private IRenderedComponent<TPage> Open<TPage>(string route, Action<ComponentParameterCollectionBuilder<TPage>>? parameters = null)
        where TPage : IComponent
    {
        Navigation.NavigateTo(route);
        IRenderedComponent<TPage> page = Render(parameters ?? (_ => { }));
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    [Fact]
    public void Le_journal_de_l_annee_liste_les_ecritures_les_plus_recentes_d_abord_et_s_equilibre()
    {
        IRenderedComponent<JournalPage> page = Open<JournalPage>("accounting/journal?from=2026-01-01&to=2026-12-31");

        page.Find("#journal-count").TextContent.Should().Be("2 écriture(s)");
        page.FindAll(".lt-journal-head").Select(r => r.TextContent).Should().SatisfyRespectively(
            first => first.Should().Contain("F2026-0002").And.Contain("Boulangerie Martin"),
            second => second.Should().Contain("F2026-0001"));
        page.Find("#journal-balanced").TextContent.Should().Contain("Équilibré");
        page.Find("#journal-total-debit").TextContent.Should().Be(ExplorerText.Money(1500m));
        page.FindAll(".lt-journal-line").Should().BeEmpty("les lignes sont repliées au départ");
    }

    [Fact]
    public void Deplier_une_ecriture_montre_ses_comptes_avec_un_lien_vers_le_grand_livre()
    {
        IRenderedComponent<JournalPage> page = Open<JournalPage>("accounting/journal?from=2026-01-01&to=2026-12-31");

        page.FindAll(".lt-disclosure")[1].Click();

        page.FindAll(".lt-journal-line").Should().HaveCount(3);
        page.FindAll(".lt-journal-line a.lt-account").Select(a => a.GetAttribute("href")).Should().Contain(
            $"accounting/accounts/411?from=2026-01-01&to=2026-12-31&customer={Dupont}");
        page.FindAll(".lt-disclosure")[1].GetAttribute("aria-expanded").Should().Be("true");

        page.Find("#journal-expand-all").Click();
        page.FindAll(".lt-journal-line").Should().HaveCount(6);
    }

    [Fact]
    public void La_recherche_et_le_compte_filtrent_le_journal()
    {
        IRenderedComponent<JournalPage> page = Open<JournalPage>("accounting/journal?from=2026-01-01&to=2026-12-31&account=707");
        page.Find("#journal-count").TextContent.Should().Be("1 écriture(s)");

        page.Find("#journal-search").Input("dupont");

        page.WaitForAssertion(() => page.Find("#journal-empty"));
        _accounting.JournalQueries.Last().Should().BeEquivalentTo(new { Search = "dupont", Account = "707" });
    }

    [Fact]
    public void Changer_de_periode_change_l_adresse()
    {
        IRenderedComponent<JournalPage> page = Open<JournalPage>("accounting/journal");

        page.Find("#period-thisyear").GetAttribute("aria-checked").Should().Be("true", "l'année en cours par défaut");
        page.Find("#period-lastyear").Click();

        Navigation.Uri.Should().Contain("from=2025-01-01").And.Contain("to=2025-12-31");
    }

    [Fact]
    public void Une_periode_refusee_affiche_l_erreur_traduite()
    {
        _accounting.NextError = ErrorCodes.Accounting.PeriodTooLong;

        IRenderedComponent<JournalPage> page = Open<JournalPage>("accounting/journal");

        this.ToastText().Should().Contain("Une consultation couvre cinq ans au plus");
    }

    [Fact]
    public void La_balance_donne_le_chiffre_d_affaires_la_TVA_et_le_detail_par_client()
    {
        IRenderedComponent<BalancePage> page = Open<BalancePage>("accounting/balance?from=2026-01-01&to=2026-12-31");

        page.Find("#balance-kpi-revenue").TextContent.Should().Contain(ExplorerText.Money(1250m));
        page.Find("#balance-kpi-vat").TextContent.Should().Contain(ExplorerText.Money(250m));
        page.Find("#balance-kpi-receivable").TextContent.Should().Contain(ExplorerText.Money(1500m));
        page.Find("#balance-account-706").GetAttribute("href").Should().Be("accounting/accounts/706?from=2026-01-01&to=2026-12-31");
        page.Find("#balance-customers-table").TextContent.Should().Contain("Dupont SARL").And.Contain("Boulangerie Martin");
    }

    [Fact]
    public void Le_grand_livre_d_un_client_part_du_solde_d_ouverture_et_cumule()
    {
        IRenderedComponent<LedgerPage> page = Open<LedgerPage>($"accounting/accounts/411?from=2026-01-01&to=2026-12-31&customer={Dupont}",
            p => p.Add(l => l.Account, "411"));

        page.Find("h1").TextContent.Should().Contain("411").And.Contain("Clients");
        page.Find("#ledger-opening").TextContent.Should().Contain(ExplorerText.Money(600m));
        page.Find("#ledger-closing").TextContent.Should().Be(ExplorerText.Money(1800m));
        page.Find("#ledger-customer-chip").TextContent.Should().Contain("Dupont SARL");
        _accounting.LedgerQueries.Single().Should().Be(("411", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), (Guid?)Dupont));
    }
}
