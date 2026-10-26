using System.Globalization;
using Bunit;
using Lettra.Contracts.Insights;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Insights;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Insights;

/// <summary>Écran « Pourquoi ce chiffre ? » : la valeur, la règle en une phrase, le calcul et les pièces, mois par mois.</summary>
[Collection("culture")]
public sealed class WhyFigurePageTests : BunitContext
{
    private static readonly DateOnly Today = new(2026, 10, 16);
    private readonly FakeInsightsGateway _insights = new();

    public WhyFigurePageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IInsightsGateway>(_insights);
        Services.AddSingleton<TimeProvider>(new ParisClock(new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
        _insights.Explanations[ExplainedFigureDto.MonthRevenue] = new(
            ExplainedFigureDto.MonthRevenue,
            1_450m,
            Today,
            new DateOnly(2026, 10, 1),
            [
                new(Guid.CreateVersion7(), "FA-2026-0010", "Atelier Dupont", ExplanationLineKindDto.Invoice, new DateOnly(2026, 10, 2), 1_000m),
                new(Guid.CreateVersion7(), "AC-2026-0002", "Martin SARL", ExplanationLineKindDto.Invoice, new DateOnly(2026, 10, 9), 500m),
                new(Guid.CreateVersion7(), "AV-2026-0001", "Martin SARL", ExplanationLineKindDto.CreditNote, new DateOnly(2026, 10, 12), -50m),
            ]);
        _insights.Explanations[ExplainedFigureDto.Overdue] = new(
            ExplainedFigureDto.Overdue,
            900m,
            Today,
            null,
            [new(Guid.CreateVersion7(), "FA-2026-0007", "Atelier Dupont", ExplanationLineKindDto.Invoice, new DateOnly(2026, 9, 1), 900m, 1_200m, 100m, 200m, 45)]);
    }

    private IRenderedComponent<WhyFigurePage> Open(string address)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(address);
        IRenderedComponent<WhyFigurePage> page = Render<WhyFigurePage>();
        page.WaitForState(() => page.FindAll("#why-result").Count > 0);
        return page;
    }

    [Fact]
    public void Le_chiffre_d_affaires_du_mois_se_deroule_jusqu_aux_pieces()
    {
        IRenderedComponent<WhyFigurePage> page = Open("why");

        _insights.Explained.Should().Equal([(ExplainedFigureDto.MonthRevenue, null)]);
        page.Find("#why-tab-MonthRevenue").GetAttribute("aria-current").Should().Be("page");
        page.Find("#why-period").TextContent.Should().Be("Octobre 2026");
        page.Find("#why-value").TextContent.Should().Contain("450,00").And.Contain("hors taxes");
        page.Find("#why-rule").TextContent.Should().Be("C'est la somme hors taxes des 2 factures et acomptes émis du 01/10/2026 au 16/10/2026, moins les 1 avoirs émis sur la même période.");
        page.FindAll("#why-equation li").Select(l => l.QuerySelector("span")!.TextContent)
            .Should().Equal("Factures et acomptes", "moins les avoirs", "Chiffre d'affaires du mois");
        page.FindAll("#why-equation strong")[1].TextContent.Should().Contain("50,00");
        page.FindAll("#why-lines li").Should().HaveCount(3);
        page.Find("#why-lines li.is-credit").TextContent.Should().Contain("avoir émis le 12/10/2026").And.Contain("se retranche");
        page.Find("#why-lines a").GetAttribute("href").Should().StartWith("invoices/");
        page.Find("#why-previous").GetAttribute("href").Should().Be("why?figure=MonthRevenue&month=2026-09");
    }

    [Fact]
    public void Un_mois_passe_est_demande_par_l_adresse()
    {
        _insights.Explanations[ExplainedFigureDto.MonthRevenue] = new(
            ExplainedFigureDto.MonthRevenue, 1_000m, Today, new DateOnly(2026, 7, 1),
            [new(Guid.CreateVersion7(), "FA-2026-0004", "Atelier Dupont", ExplanationLineKindDto.Invoice, new DateOnly(2026, 7, 2), 1_000m)]);

        IRenderedComponent<WhyFigurePage> page = Open("why?figure=MonthRevenue&month=2026-07");

        _insights.Explained.Should().Equal([(ExplainedFigureDto.MonthRevenue, (DateOnly?)new DateOnly(2026, 7, 1))]);
        page.Find("#why-rule").TextContent.Should().Be("C'est la somme hors taxes des 1 factures et acomptes émis du 01/07/2026 au 31/07/2026. Aucun avoir n'a été émis sur la même période.");
    }

    [Fact]
    public void Le_montant_en_retard_montre_le_calcul_de_chaque_facture()
    {
        IRenderedComponent<WhyFigurePage> page = Open("why?figure=overdue");

        _insights.Explained.Should().Equal([(ExplainedFigureDto.Overdue, null)]);
        page.FindAll("#why-period").Should().BeEmpty("le retard ne se lit pas par mois");
        page.Find("#why-rule").TextContent.Should().Contain("1 factures dont l'échéance est passée au 16/10/2026");
        page.FindAll("#why-equation li").Should().HaveCount(4);
        string line = page.Find("#why-lines li").TextContent;
        line.Should().Contain("échue le 01/09/2026, en retard de 45 j").And.Contain("200,00").And.Contain("900,00");
    }

    [Fact]
    public void La_recherche_filtre_les_pieces_et_un_mois_vide_le_dit()
    {
        IRenderedComponent<WhyFigurePage> page = Open("why");

        page.Find("#why-search").Input("martin");

        page.FindAll("#why-lines li").Should().HaveCount(2);
        page.Find("#why-count").TextContent.Should().Be("2 pièces affichées sur 3");

        _insights.Explanations[ExplainedFigureDto.MonthRevenue] = new(ExplainedFigureDto.MonthRevenue, 0m, Today, new DateOnly(2026, 11, 1), []);
        Services.GetRequiredService<NavigationManager>().NavigateTo("why?month=2026-11");
        page.WaitForState(() => page.FindAll("#why-empty").Count > 0);
        page.Find("#why-rule").TextContent.Should().Be("Novembre 2026 n'a pas encore commencé : aucune pièce ne compte encore.");
    }

    [Fact]
    public void Un_refus_s_affiche_sans_resultat()
    {
        _insights.Explanations.Clear();

        IRenderedComponent<WhyFigurePage> page = Render<WhyFigurePage>();

        page.WaitForAssertion(() => page.FindAll(".lt-skeleton").Should().BeEmpty());
        page.FindAll("#why-result").Should().BeEmpty();
        _insights.Explained.Should().ContainSingle();
    }
}
