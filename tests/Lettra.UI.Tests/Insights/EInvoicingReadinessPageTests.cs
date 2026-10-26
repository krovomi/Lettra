using System.Globalization;
using Bunit;
using Lettra.Contracts.Insights;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Insights;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Insights;

/// <summary>Écran « Prêt pour la facture électronique ? » : le score, chaque contrôle et ce qu'il reste à corriger, avec un lien.</summary>
[Collection("culture")]
public sealed class EInvoicingReadinessPageTests : BunitContext
{
    private static readonly DateOnly Today = new(2026, 10, 26);
    private static readonly Guid Dupont = Guid.CreateVersion7();
    private static readonly Guid Rejected = Guid.CreateVersion7();
    private readonly FakeInsightsGateway _insights = new();

    public EInvoicingReadinessPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IInsightsGateway>(_insights);
        Services.AddSingleton<TimeProvider>(new ParisClock(new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private static EInvoicingCheckResponse Ok(EInvoicingCheckDto check, int total = 1) => new(check, total, total, 100, []);

    [Fact]
    public void Le_score_et_les_controles_a_revoir_avec_leurs_liens()
    {
        _insights.Readiness = new EInvoicingReadinessResponse(
            Today,
            new DateOnly(2026, 7, 28),
            67,
            [
                Ok(EInvoicingCheckDto.CompanyIdentity),
                new(EInvoicingCheckDto.CompanyVat, 1, 0, 0, []),
                Ok(EInvoicingCheckDto.CompanyIban),
                new(EInvoicingCheckDto.FrenchCustomers, 3, 2, 66, [new(Dupont, "Atelier Dupont")]),
                Ok(EInvoicingCheckDto.EuropeanCustomers, 0),
                Ok(EInvoicingCheckDto.Suppliers, 4),
                new(EInvoicingCheckDto.Transmissions, 2, 1, 50, [new(Rejected, "FA-2026-0042")]),
            ]);

        IRenderedComponent<EInvoicingReadinessPage> page = Render<EInvoicingReadinessPage>();
        page.WaitForState(() => page.FindAll("#ready-score").Count > 0);

        page.Find("#ready-score").TextContent.Should().Be("67");
        page.Find("#ready-score-label").TextContent.Should().Be("À mi-chemin");
        page.Find("#ready-summary").TextContent.Should().Be("3 contrôles restent à satisfaire.");
        page.Find("#ready-head").TextContent.Should().Contain("28/07/2026");
        page.FindAll("#ready-checks > li").Should().HaveCount(7);
        page.Find("#ready-companyvat").TextContent.Should().Contain("Manquant").And.Contain("Compléter la fiche entreprise");
        page.Find("#ready-companyvat a.lettra-button").GetAttribute("href").Should().Be("settings/company");
        page.Find("#ready-frenchcustomers").TextContent.Should().Contain("2 sur 3");
        page.Find("#ready-frenchcustomers .lt-ready-gaps a").GetAttribute("href").Should().Be($"customers/{Dupont}/edit");
        page.Find("#ready-europeancustomers").TextContent.Should().Contain("Non concerné");
        page.Find("#ready-europeancustomers").ClassList.Should().Contain("is-ok");
        page.FindAll("#ready-europeancustomers a").Should().BeEmpty();
        page.Find("#ready-transmissions .lt-ready-gaps a").GetAttribute("href").Should().Be($"invoices/{Rejected}");
    }

    [Fact]
    public void Un_dossier_pret_le_dit()
    {
        _insights.Readiness = new EInvoicingReadinessResponse(Today, Today.AddDays(-90), 100, [.. Enum.GetValues<EInvoicingCheckDto>().Select(c => Ok(c))]);

        IRenderedComponent<EInvoicingReadinessPage> page = Render<EInvoicingReadinessPage>();
        page.WaitForState(() => page.FindAll("#ready-score").Count > 0);

        page.Find("#ready-score-label").TextContent.Should().Be("Dossier prêt");
        page.Find("#ready-summary").TextContent.Should().Be("Tous les contrôles sont satisfaits.");
        page.FindAll(".lt-ready-gaps").Should().BeEmpty();
    }

    [Fact]
    public void Une_erreur_s_affiche_sans_squelette_infini()
    {
        IRenderedComponent<EInvoicingReadinessPage> page = Render<EInvoicingReadinessPage>();

        page.WaitForAssertion(() => page.FindAll(".lt-skeleton, [aria-busy='true']").Should().BeEmpty());
        page.FindAll("#ready-score").Should().BeEmpty();
    }
}
