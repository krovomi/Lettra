using System.Globalization;
using Bunit;
using Lettra.Contracts.Accounting;
using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Accounting;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Consultation;

/// <summary>Révision avant clôture : compteurs par gravité, contrôles traduits, liens vers les pièces, filtre et dépliage.</summary>
[Collection("culture")]
public sealed class ClosingReviewPageTests : BunitContext
{
    private static readonly Guid Martin = new("0199a000-0000-7000-8000-000000000001");
    private static readonly Guid Invoice = new("0199a000-0000-7000-8000-000000000002");

    private readonly FakeAccountingGateway _accounting = new();

    public ClosingReviewPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IAccountingGateway>(_accounting);
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
        _accounting.Review = new ClosingReviewResponse(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31),
        [
            new ReviewCheckResponse("CustomersInCredit", ReviewSeverityDto.Warning, 1, 30m, null,
                [new ReviewItemResponse(ReviewItemKindDto.Customer, Martin, "Boulangerie Martin", 30m, null)]),
            new ReviewCheckResponse("DoubtfulReceivables", ReviewSeverityDto.Warning, 7, 700m, null,
                [.. Enumerable.Range(1, 7).Select(i => new ReviewItemResponse(ReviewItemKindDto.Invoice, i == 1 ? Invoice : Guid.CreateVersion7(), $"FAC-00000{i} · Client", 100m, new DateOnly(2025, 3, i), 300 - i))]),
            new ReviewCheckResponse("BankUnreconciled", ReviewSeverityDto.Info, 2, null, null,
                [new ReviewItemResponse(ReviewItemKindDto.Bank, null, string.Empty, null, new DateOnly(2026, 1, 4))]),
            new ReviewCheckResponse("OldDrafts", ReviewSeverityDto.Info, 1, null, null, [new ReviewItemResponse(ReviewItemKindDto.Invoice, null, "Dupont SARL", null, null)]),
            new ReviewCheckResponse("VatRate", ReviewSeverityDto.Ok, 0, 1000m, 20m, []),
            new ReviewCheckResponse("DisputedReceivables", ReviewSeverityDto.Ok, 0, null, null, []),
            new ReviewCheckResponse("DownPaymentsUnapplied", ReviewSeverityDto.Ok, 0, null, null, []),
        ]);
    }

    private IRenderedComponent<ClosingReviewPage> Open(string route = "accounting/review")
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(route);
        IRenderedComponent<ClosingReviewPage> page = Render<ClosingReviewPage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    [Fact]
    public void Sans_date_la_revision_de_la_derniere_cloture_compte_les_controles_par_gravite()
    {
        IRenderedComponent<ClosingReviewPage> page = Open();

        _accounting.ReviewQueries.Should().Equal((DateOnly?)null);
        page.Find("#review-period").TextContent.Should().Be("Révision au 31 décembre 2025");
        page.Find("#review-year").TextContent.Should().Contain("1 janvier 2025");
        page.Find("#review-count-warning dd").TextContent.Should().Be("2");
        page.Find("#review-count-info dd").TextContent.Should().Be("2");
        page.Find("#review-count-ok dd").TextContent.Should().Be("3");
        page.Find("#tab-review").ClassList.Should().Contain("active");
        page.Find("#tab-journal").GetAttribute("href").Should().Be("accounting/journal?from=2025-01-01&to=2025-12-31");
        page.Find("#review-limits").TextContent.Should().Contain("expert-comptable");
    }

    [Fact]
    public void Chaque_anomalie_mene_a_ses_pieces()
    {
        IRenderedComponent<ClosingReviewPage> page = Open();

        page.Find("#review-CustomersInCredit").ClassList.Should().Contain("is-warning");
        page.Find("#review-CustomersInCredit h3").TextContent.Should().Be("Clients créditeurs");
        page.Find("#review-CustomersInCredit .lt-review-detail").TextContent.Should().Contain("1 client(s)").And.Contain("30");
        page.Find("#review-CustomersInCredit .lt-review-items a").GetAttribute("href").Should()
            .Be($"accounting/accounts/411?from=2025-01-01&to=2025-12-31&customer={Martin}");
        page.Find("#review-DoubtfulReceivables .lt-review-items a").GetAttribute("href").Should().Be($"invoices/{Invoice}");
        page.Find("#review-DoubtfulReceivables .lt-review-items li").TextContent.Should().Contain("299 j de retard");
        page.Find("#review-BankUnreconciled .lt-review-items a").GetAttribute("href").Should().Be("bank");
        page.Find("#review-BankUnreconciled .lt-review-items a").TextContent.Should().Contain("4 janvier 2026");
        page.Find("#review-OldDrafts .lt-review-items a").GetAttribute("href").Should().Be("invoices");
        page.Find("#review-VatRate .lt-review-detail").TextContent.Should().Contain("20,00").And.Contain("cohérente");
    }

    [Fact]
    public void Une_longue_liste_se_replie_et_se_deplie()
    {
        IRenderedComponent<ClosingReviewPage> page = Open();

        page.FindAll("#review-DoubtfulReceivables .lt-review-items li").Should().HaveCount(5);
        page.Find("#review-DoubtfulReceivables-more").TextContent.Should().Contain("Tout afficher (7)");
        page.Find("#review-DoubtfulReceivables-more").Click();
        page.FindAll("#review-DoubtfulReceivables .lt-review-items li").Should().HaveCount(7);
        page.Find("#review-DoubtfulReceivables-more").GetAttribute("aria-expanded").Should().Be("true");
        page.Find("#review-DoubtfulReceivables-more").Click();
        page.FindAll("#review-DoubtfulReceivables .lt-review-items li").Should().HaveCount(5);
        page.FindAll("#review-CustomersInCredit-more").Should().BeEmpty();
    }

    [Fact]
    public void Le_filtre_par_gravite_garde_les_controles_choisis()
    {
        IRenderedComponent<ClosingReviewPage> page = Open();

        page.Find("#review-severity").Change("Info");
        page.FindAll("#review-checks > li").Select(li => li.Id).Should().Equal("review-BankUnreconciled", "review-OldDrafts");
        page.Find("#review-filters-reset").Click();
        page.FindAll("#review-checks > li").Should().HaveCount(7);
    }

    [Fact]
    public void Une_date_choisie_ou_aujourd_hui_change_l_adresse_et_la_revision_demandee()
    {
        IRenderedComponent<ClosingReviewPage> page = Open("accounting/review?closing=2026-06-30");

        _accounting.ReviewQueries.Should().Equal(new DateOnly(2026, 6, 30));
        page.Find("#review-today").GetAttribute("href").Should().Be("accounting/review?closing=2026-10-09");
        page.Find("#review-last-closing").GetAttribute("href").Should().Be("accounting/review");
        page.Find("#review-closing").Change("31/03/2026");
        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("accounting/review?closing=2026-03-31");
    }

    [Fact]
    public void Un_refus_du_serveur_s_affiche_et_la_page_reste_lisible()
    {
        _accounting.NextError = ErrorCodes.Api.Unreachable;

        IRenderedComponent<ClosingReviewPage> page = Open();

        page.FindAll(".lt-review-check").Should().BeEmpty();
        page.Find("#review-empty").Should().NotBeNull();
        Services.GetRequiredService<ToastService>().Visible.Should().ContainSingle();
    }
}
