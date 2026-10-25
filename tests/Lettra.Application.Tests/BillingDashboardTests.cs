using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Kpi;
using Lettra.Contracts.Billing;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Tests;

/// <summary>Les chiffres de l'accueil du dossier, calculés au serveur sur toutes les pièces validées.</summary>
public sealed class BillingDashboardTests
{
    private static readonly DateOnly Today = new(2026, 10, 16);

    private static InvoiceKpiFact Invoice(DateOnly issued, decimal exclTax, InvoiceKind kind = InvoiceKind.Invoice) =>
        new(kind, issued, issued.AddDays(30), Guid.Empty, exclTax, exclTax * 1.2m, 0m, 0m);

    private static QuoteKpiFact Quote(QuoteStatus status, decimal exclTax, DateOnly? validUntil = null) =>
        new(status, new DateOnly(2026, 10, 1), validUntil ?? new DateOnly(2026, 10, 31), exclTax);

    private static BillingDashboardResponse Compute(IEnumerable<InvoiceKpiFact> invoices, IEnumerable<QuoteKpiFact>? quotes = null) =>
        BillingDashboardCalculator.Compute(new BillingKpiFacts([.. invoices], [.. quotes ?? []]), Today);

    [Fact]
    public void Le_chiffre_d_affaires_couvre_six_mois_et_deduit_les_avoirs()
    {
        BillingDashboardResponse dashboard = Compute(
        [
            Invoice(new DateOnly(2026, 10, 2), 1000m),
            Invoice(new DateOnly(2026, 10, 9), 200m, InvoiceKind.CreditNote),
            Invoice(new DateOnly(2026, 10, 10), 300m, InvoiceKind.DownPayment),
            Invoice(new DateOnly(2026, 9, 30), 500m),
            Invoice(new DateOnly(2026, 4, 30), 9999m),
            Invoice(new DateOnly(2026, 10, 20), 7777m),
        ]);

        dashboard.Months.Select(m => m.Month).Should().Equal(
            new DateOnly(2026, 5, 1), new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1),
            new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1));
        dashboard.Months[^1].ExclTax.Should().Be(1100m);
        dashboard.Months[^2].ExclTax.Should().Be(500m);
        dashboard.Months.Sum(m => m.ExclTax).Should().Be(1600m, "avril est hors de la période et le 20 octobre n'est pas encore arrivé");
        dashboard.InvoicesThisMonth.Should().Be(2, "un avoir n'est pas une facture du mois");
    }

    [Fact]
    public void Un_devis_attend_une_reponse_tant_qu_il_est_valide_et_pas_expire()
    {
        BillingDashboardResponse dashboard = Compute([],
        [
            Quote(QuoteStatus.Validated, 1000m),
            Quote(QuoteStatus.Validated, 300m, Today),
            Quote(QuoteStatus.Validated, 7000m, Today.AddDays(-1)),
            Quote(QuoteStatus.Accepted, 4000m),
            Quote(QuoteStatus.Refused, 2000m),
        ]);

        dashboard.PendingQuotes.Should().Be(2, "un devis valable jusqu'à aujourd'hui attend encore");
        dashboard.PendingQuotesExclTax.Should().Be(1300m);
    }

    [Fact]
    public void Un_dossier_vide_donne_six_mois_a_zero()
    {
        BillingDashboardResponse dashboard = Compute([]);

        dashboard.Months.Should().HaveCount(BillingDashboardResponse.MonthCount).And.OnlyContain(m => m.ExclTax == 0m);
        dashboard.InvoicesThisMonth.Should().Be(0);
        dashboard.PendingQuotes.Should().Be(0);
    }

    [Fact]
    public async Task La_requete_lit_les_pieces_et_calcule_au_jour_legal()
    {
        // 22 h 30 UTC le 31 octobre : déjà le 1er novembre à Paris.
        BillingDashboardHandler handler = new(new FixedReader(new BillingKpiFacts([Invoice(new DateOnly(2026, 11, 1), 100m)], [])),
            new FixedClock(new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.Zero)));

        Result<BillingDashboardResponse> result = await handler.HandleAsync(new GetBillingDashboardQuery(new DossierId(Guid.CreateVersion7())), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Months[^1].Should().Be(new MonthRevenueDto(new DateOnly(2026, 11, 1), 100m));
    }

    [Fact]
    public void La_requete_demande_le_droit_de_lire_le_dossier() =>
        new GetBillingDashboardQuery(new DossierId(Guid.CreateVersion7())).Permission.Should().Be(Permission.DossierLire);

    private sealed class FixedReader(BillingKpiFacts facts) : IBillingKpiReader
    {
        public Task<BillingKpiFacts> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(facts);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
