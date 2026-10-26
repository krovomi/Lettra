using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Lettra.Contracts.Tax;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Tax;
using Lettra.UI.Tests.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Tax;

/// <summary>Délais de paiement : tableau du rapport de gestion, échéances au-delà du plafond légal, factures non réglées.</summary>
[Collection("culture")]
public sealed class PaymentDelaysPageTests : BunitContext
{
    private readonly FakeTaxGateway _tax = new();

    public PaymentDelaysPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<ITaxGateway>(_tax);
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<PaymentDelaysPage> Open(string route = "tax/payment-delays")
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(route);
        IRenderedComponent<PaymentDelaysPage> page = Render<PaymentDelaysPage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    [Fact]
    public void Sans_date_le_tableau_de_la_derniere_cloture_suit_le_modele_du_rapport_de_gestion()
    {
        IRenderedComponent<PaymentDelaysPage> page = Open();

        _tax.DelayQueries.Should().Equal((DateOnly?)null);
        page.Find("#delays-period").TextContent.Should().Be("Situation au 31 décembre 2025");
        page.Find("#delays-year").TextContent.Should().Contain("1 janvier 2025").And.Contain("100");
        page.Find("#delays-table thead").TextContent.Should()
            .Contain("0 jour (indicatif)").And.Contain("1 à 30 jours").And.Contain("91 jours et plus").And.Contain("Total (1 jour et plus)");
        page.Find("#delays-count-1").TextContent.Should().Be("2");
        page.Find("#delays-count-total").TextContent.Should().Be("3");
        page.Find("#delays-amount-total").TextContent.Should().Contain("4").And.Contain("200").And.Contain("€");
        page.Find("#delays-percent-total").TextContent.Should().Contain("4,2");
        page.Find("#delays-part-b").TextContent.Should().Contain("1 facture(s)").And.Contain("800");
        page.Find("#delays-part-c").TextContent.Should().Contain("30 jours");
        page.Find("#delays-not-due").TextContent.Should().Contain("2");
        page.FindAll("#delays-cards li").Should().HaveCount(6);
        page.Find("#delays-cards li.is-total").TextContent.Should().Contain("Total (1 jour et plus)").And.Contain("3 facture(s)");
        page.Find("#tab-payment-delays").ClassList.Should().Contain("active");
    }

    [Fact]
    public void Une_echeance_au_dela_du_plafond_legal_est_signalee()
    {
        IRenderedComponent<PaymentDelaysPage> page = Open();

        page.Find("#delays-breach-banner").TextContent.Should().Contain("1 facture(s)");
        IElement row = page.Find("#delays-breaches tbody tr");
        row.TextContent.Should().Contain("FAC-000107").And.Contain("Cave Leroy").And.Contain("30 j");
        row.QuerySelector("a")!.GetAttribute("href").Should().Be("invoices/0199a000-0000-7000-8000-000000000107");
    }

    [Fact]
    public void Sans_depassement_ni_facture_due_l_ecran_le_dit()
    {
        _tax.Delays = FakeTaxGateway.SampleDelays(new DateOnly(2025, 12, 31)) with { Invoices = [], TermBreaches = [] };

        IRenderedComponent<PaymentDelaysPage> page = Open();

        page.FindAll("#delays-breach-banner").Should().BeEmpty();
        page.Find("#delays-breaches-empty").TextContent.Should().Contain("respectent le plafond légal");
        page.Find("#delays-invoices-empty").TextContent.Should().Contain("Tout était réglé");
    }

    [Fact]
    public void Les_factures_non_reglees_se_trient_et_se_filtrent_par_tranche()
    {
        IRenderedComponent<PaymentDelaysPage> page = Open();

        Numbers(page).Should().Equal("FAC-000101", "FAC-000106", "FAC-000103", "FAC-000102", "FAC-000104", "FAC-000105");
        page.Find("#delays-invoices tbody tr[data-invoice='0199a000-0000-7000-8000-000000000106']").TextContent.Should().Contain("Litigieuse");
        page.Find("#delays-invoices tbody tr[data-invoice='0199a000-0000-7000-8000-000000000105']").TextContent.Should().Contain("Non échue");

        page.Find("#delays-bracket").Change("1");
        Numbers(page).Should().Equal("FAC-000103", "FAC-000102");

        page.Find("#delays-bracket").Change("late");
        Numbers(page).Should().Equal("FAC-000101", "FAC-000103", "FAC-000102");

        page.Find("#delays-bracket").Change("not-due");
        Numbers(page).Should().Equal("FAC-000105");

        page.Find("#delays-bracket").Change("disputed");
        Numbers(page).Should().Equal("FAC-000106");

        page.Find("#delays-bracket").Change("");
        page.Find("#delays-search").Input("durand");
        Numbers(page).Should().Equal("FAC-000101", "FAC-000104");

        page.Find("#delays-search").Input("personne");
        page.Find("#delays-empty-reset").Click();
        Numbers(page).Should().HaveCount(6);

        page.Find("#delays-invoices-sort-amount").Click();
        Numbers(page)[0].Should().BeOneOf("FAC-000103", "FAC-000105");
    }

    [Fact]
    public void Une_date_choisie_recharge_le_tableau_a_cette_date()
    {
        IRenderedComponent<PaymentDelaysPage> page = Open("tax/payment-delays?closing=2026-06-30");

        _tax.DelayQueries.Should().Equal(new DateOnly(2026, 6, 30));
        page.Find("#delays-period").TextContent.Should().Be("Situation au 30 juin 2026");
        page.Find("#delays-today").GetAttribute("href").Should().Be("tax/payment-delays?closing=2026-10-09");

        page.Find("#delays-closing").Change("31/03/2026");
        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("tax/payment-delays?closing=2026-03-31");
    }

    [Fact]
    public void Un_refus_du_serveur_s_affiche_avec_un_tableau_vide()
    {
        _tax.Delays = null;

        IRenderedComponent<PaymentDelaysPage> page = Open();

        page.Find("#delays-count-total").TextContent.Should().Be("0");
    }

    [Fact]
    public void Au_dela_de_vingt_cinq_factures_la_liste_se_pagine()
    {
        DateOnly closing = new(2025, 12, 31);
        _tax.Delays = FakeTaxGateway.SampleDelays(closing) with
        {
            Invoices = [.. Enumerable.Range(1, 30).Select(n => new PaymentDelayInvoiceResponse(
                Guid.CreateVersion7(), $"FAC-{n:D6}", "Atelier Durand", closing.AddDays(-60), closing.AddDays(-n), n, 1, 100m, false))],
        };

        IRenderedComponent<PaymentDelaysPage> page = Open();

        Numbers(page).Should().HaveCount(25);
        Numbers(page)[0].Should().Be("FAC-000030");
        page.Find("#delays-previous").HasAttribute("disabled").Should().BeTrue();
        page.Find("#delays-next").Click();
        Numbers(page).Should().HaveCount(5);
        page.Find("#delays-search").Input("FAC-000001");
        Numbers(page).Should().Equal("FAC-000001");
        page.FindAll("#delays-pager").Should().BeEmpty();
    }

    private static List<string> Numbers(IRenderedComponent<PaymentDelaysPage> page) =>
        [.. page.FindAll("#delays-invoices tbody tr td:first-child a").Select(a => a.TextContent)];
}
