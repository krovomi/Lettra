using System.Globalization;
using Bunit;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Customers;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Onboarding;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Billing;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Onboarding;

/// <summary>Liste de démarrage du tableau de bord (vague 5, lane F) : chaque étape se coche toute seule.</summary>
[Collection("culture")]
public sealed class StarterChecklistTests : BunitContext
{
    private static readonly DocumentLineRequest Conseil = new() { Designation = "Conseil", UnitCode = "HUR", Quantity = 2m, UnitPriceExclTax = 100m };

    private readonly FakeBillingSettingsGateway _settings = new() { Company = null };
    private readonly FakeCustomersGateway _customers = new();
    private readonly FakeQuotesGateway _quotes = new();
    private readonly BunitJSModuleInterop _ui;

    public StarterChecklistTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ui = JSInterop.SetupModule(UiModule.Path);
        Services.AddLettraUi();
        Services.AddSingleton<IBillingSettingsGateway>(_settings);
        Services.AddSingleton<ICustomersGateway>(_customers);
        Services.AddSingleton<IQuotesGateway>(_quotes);
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    [Fact]
    public void Un_dossier_vide_montre_les_cinq_etapes_a_faire()
    {
        IRenderedComponent<StarterChecklist> liste = Render<StarterChecklist>();

        liste.WaitForAssertion(() => liste.Find("#starter-count").TextContent.Should().Be("0 sur 5"));
        liste.FindAll(".lt-starter-steps a").Select(a => (a.TextContent, a.GetAttribute("href"))).Should().Equal(
            ("Compléter la fiche de l'entreprise", "settings/company"),
            ("Créer un premier client", "customers"),
            ("Faire un premier devis", "quotes/new"),
            ("Transformer un devis en facture", "quotes"),
            ("Exporter le FEC", "exports/fec"));
    }

    [Fact]
    public void Les_etapes_se_cochent_d_apres_les_donnees_du_dossier()
    {
        _settings.Company = BillingFixtures.Company();
        _customers.Customers.Add(new CustomerSummary(BillingFixtures.DupontId, CustomerKindDto.Company, "Dupont SARL", "732829320", "Lyon"));
        _quotes.Add(QuoteStatusDto.Invoiced, "DEV-2026-0001", Conseil);
        _ui.Setup<string?>("readPreference", i => ((string)i.Arguments[0]!).StartsWith("lettra.starter.", StringComparison.Ordinal)).SetResult("fec");

        IRenderedComponent<StarterChecklist> liste = Render<StarterChecklist>();

        liste.WaitForAssertion(() => liste.Markup.Trim().Should().BeEmpty("tout est fait : la liste disparaît"));
    }

    [Fact]
    public void Une_etape_faite_est_barree_et_annoncee()
    {
        _customers.Customers.Add(new CustomerSummary(BillingFixtures.DupontId, CustomerKindDto.Company, "Dupont SARL", "732829320", "Lyon"));

        IRenderedComponent<StarterChecklist> liste = Render<StarterChecklist>();

        liste.WaitForAssertion(() => liste.Find("#starter-count").TextContent.Should().Be("1 sur 5"));
        liste.Find("#starter-customer").ClassList.Should().Contain("is-done");
        liste.Find("#starter-customer").TextContent.Should().Contain("Créer un premier client").And.Contain("fait");
        liste.Find("progress").GetAttribute("value").Should().Be("1");
    }

    [Fact]
    public async Task L_export_FEC_et_le_masquage_sont_retenus_sur_l_appareil()
    {
        StarterProgress progress = Services.GetRequiredService<StarterProgress>();
        await progress.MarkFecExportedAsync();

        IRenderedComponent<StarterChecklist> liste = Render<StarterChecklist>();
        liste.WaitForAssertion(() => liste.Find("#starter-fec").ClassList.Should().Contain("is-done"));
        liste.Find("#starter-hide").Click();

        liste.Markup.Trim().Should().BeEmpty();
        _ui.Invocations.Where(i => i.Identifier == "writePreference").Select(i => (string)i.Arguments[1]!).Should().Equal("fec", "fec;hidden");
        _ui.Invocations.Last(i => i.Identifier == "writePreference").Arguments[0].Should().Be("lettra.starter.local");
    }
}
