using Lettra.Calculations;
using Lettra.Contracts.Accounting;
using Lettra.Contracts.Tax;
using Lettra.Domain.Billing;
using Lettra.Exports.Application;
using Lettra.Exports.Application.Accounting;
using Lettra.Exports.Application.Banking;

namespace Lettra.Exports.Tests.Accounting;

/// <summary>Révision assistée avant la clôture : chaque contrôle, sa gravité et les pièces qui l'expliquent.</summary>
public sealed class ClosingReviewTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateOnly Closing = new(2026, 12, 31);
    private static readonly Guid Martin = new("0199a000-0000-7000-8000-000000000001");
    private static readonly Guid Durand = new("0199a000-0000-7000-8000-000000000002");

    private static readonly BankReconciliationStatus BankClean = new(null, null, 0, null, null);

    private static SalesJournalExportEntry Sale(DateOnly date, Guid customer, decimal net, decimal vat, string piece = "FAC-000001") =>
        new(Guid.CreateVersion7(), InvoiceKind.Invoice, piece, date, customer, customer == Martin ? "Martin" : "Durand",
        [
            new AccountingEntryLine(SalesAccounts.Customers, net + vat, 0m),
            new AccountingEntryLine(SalesAccounts.ServicesRevenue, 0m, net, 20m),
            new AccountingEntryLine(SalesAccounts.VatCollected, 0m, vat, 20m),
        ]);

    private static SalesJournalExportEntry Moves(DateOnly date, Guid customer, params AccountingEntryLine[] lines) =>
        new(Guid.CreateVersion7(), InvoiceKind.Invoice, "BQ", date, customer, customer == Martin ? "Martin" : "Durand", lines, "BQ");

    private static PaymentDelaysResponse Delays(params PaymentDelayInvoiceResponse[] invoices) =>
        new(Start, Closing, 0m, [], new PaymentDelayBracketResponse(-1, 0, 0m, null), 0, 0m, null, 0, 0m, invoices, []);

    private static PaymentDelayInvoiceResponse Unpaid(string number, int daysLate, decimal outstanding, bool disputed = false) =>
        new(Guid.CreateVersion7(), number, "Client " + number, Closing.AddDays(-daysLate - 30), Closing.AddDays(-daysLate), daysLate,
            disputed ? null : 4, outstanding, disputed);

    private static ClosingReviewResponse Review(
        IReadOnlyList<SalesJournalExportEntry>? entries = null,
        PaymentDelaysResponse? delays = null,
        BankReconciliationStatus? bank = null,
        IReadOnlyList<string>? drafts = null) =>
        ClosingReview.Compute(new ClosingReviewInput(entries ?? [], delays ?? Delays(), bank ?? BankClean, drafts ?? []), Start, Closing);

    private static ReviewCheckResponse Check(ClosingReviewResponse review, string code) => review.Checks.Single(c => c.Code == code);

    [Fact]
    public void Un_dossier_sans_anomalie_rend_chaque_controle_sans_anomalie()
    {
        ClosingReviewResponse review = Review([Sale(new(2026, 3, 1), Martin, 1000m, 200m)]);

        review.ClosingDate.Should().Be(Closing);
        review.FiscalYearStart.Should().Be(Start);
        review.Checks.Select(c => c.Code).Should().BeEquivalentTo(
            ClosingReview.CustomersInCredit, ClosingReview.DownPaymentsUnapplied, ClosingReview.DoubtfulReceivables,
            ClosingReview.DisputedReceivables, ClosingReview.BankUnreconciled, ClosingReview.VatRate, ClosingReview.OldDrafts);
        review.Checks.Should().OnlyContain(c => c.Severity == ReviewSeverityDto.Ok && c.Count == 0 && c.Items.Count == 0);
        Check(review, ClosingReview.VatRate).Measure.Should().Be(20m);
        Check(review, ClosingReview.VatRate).Amount.Should().Be(1000m);
    }

    [Fact]
    public void Un_client_crediteur_au_411_a_la_cloture_est_a_traiter_avec_son_trop_percu()
    {
        ClosingReviewResponse review = Review(
        [
            Sale(new(2026, 3, 1), Martin, 100m, 20m),
            Moves(new(2026, 4, 1), Martin, new AccountingEntryLine("512", 150m, 0m), new AccountingEntryLine(SalesAccounts.Customers, 0m, 150m)),
            Sale(new(2026, 3, 1), Durand, 100m, 20m),
            Moves(new(2027, 1, 5), Durand, new AccountingEntryLine("512", 500m, 0m), new AccountingEntryLine(SalesAccounts.Customers, 0m, 500m)),
        ]);

        ReviewCheckResponse check = Check(review, ClosingReview.CustomersInCredit);
        check.Severity.Should().Be(ReviewSeverityDto.Warning);
        check.Count.Should().Be(1, "le trop-perçu de Durand est postérieur à la clôture");
        check.Amount.Should().Be(30m);
        check.Items.Should().ContainSingle().Which.Should().Be(new ReviewItemResponse(ReviewItemKindDto.Customer, Martin, "Martin", 30m, null));
        review.Checks[0].Should().BeSameAs(check, "les contrôles à traiter passent en tête");
    }

    [Fact]
    public void Un_acompte_encore_au_4191_est_a_regarder()
    {
        ClosingReviewResponse review = Review(
        [
            Moves(new(2026, 6, 1), Durand, new AccountingEntryLine(SalesAccounts.Customers, 600m, 0m), new AccountingEntryLine(SalesAccounts.DownPaymentsReceived, 0m, 500m), new AccountingEntryLine(SalesAccounts.VatCollected, 0m, 100m)),
            Moves(new(2026, 5, 1), Martin, new AccountingEntryLine(SalesAccounts.Customers, 120m, 0m), new AccountingEntryLine(SalesAccounts.DownPaymentsReceived, 0m, 100m), new AccountingEntryLine(SalesAccounts.VatCollected, 0m, 20m)),
            Moves(new(2026, 9, 1), Martin, new AccountingEntryLine(SalesAccounts.DownPaymentsReceived, 100m, 0m), new AccountingEntryLine(SalesAccounts.Customers, 0m, 100m)),
        ]);

        ReviewCheckResponse check = Check(review, ClosingReview.DownPaymentsUnapplied);
        check.Severity.Should().Be(ReviewSeverityDto.Info);
        check.Items.Should().ContainSingle().Which.Id.Should().Be(Durand);
        check.Amount.Should().Be(500m);
    }

    [Fact]
    public void Les_creances_de_plus_de_six_mois_et_les_creances_contestees_sont_a_traiter()
    {
        ClosingReviewResponse review = Review(delays: Delays(
            Unpaid("FAC-000001", 181, 300m),
            Unpaid("FAC-000002", 180, 200m),
            Unpaid("FAC-000003", 400, 1000m, disputed: true),
            Unpaid("FAC-000004", 250, 50m)));

        ReviewCheckResponse doubtful = Check(review, ClosingReview.DoubtfulReceivables);
        doubtful.Severity.Should().Be(ReviewSeverityDto.Warning);
        doubtful.Items.Select(i => (i.Label, i.Days)).Should().Equal(("FAC-000004 · Client FAC-000004", 250), ("FAC-000001 · Client FAC-000001", 181));
        doubtful.Amount.Should().Be(350m);

        ReviewCheckResponse disputed = Check(review, ClosingReview.DisputedReceivables);
        disputed.Severity.Should().Be(ReviewSeverityDto.Warning);
        disputed.Items.Should().ContainSingle().Which.Kind.Should().Be(ReviewItemKindDto.Invoice);
        disputed.Amount.Should().Be(1000m);
    }

    [Theory]
    [InlineData(2026, 12, 15, ReviewSeverityDto.Warning)]
    [InlineData(2027, 1, 10, ReviewSeverityDto.Info)]
    public void Les_operations_bancaires_a_rapprocher_sont_a_traiter_si_elles_precedent_la_cloture(int year, int month, int day, ReviewSeverityDto expected)
    {
        DateOnly oldest = new(year, month, day);
        ClosingReviewResponse review = Review(bank: new BankReconciliationStatus(null, null, 3, oldest, null));

        ReviewCheckResponse check = Check(review, ClosingReview.BankUnreconciled);
        check.Severity.Should().Be(expected);
        check.Count.Should().Be(3);
        check.Items.Should().ContainSingle().Which.Should().Be(new ReviewItemResponse(ReviewItemKindDto.Bank, null, string.Empty, null, oldest));
    }

    [Fact]
    public void Un_taux_apparent_de_tva_au_dessus_du_taux_normal_est_a_traiter()
    {
        ClosingReviewResponse review = Review(
        [
            Sale(new(2026, 2, 1), Martin, 1000m, 250m),
            Sale(new(2025, 12, 1), Durand, 1000m, 0m),
        ]);

        ReviewCheckResponse check = Check(review, ClosingReview.VatRate);
        check.Severity.Should().Be(ReviewSeverityDto.Warning);
        check.Measure.Should().Be(25m, "seules les écritures de l'exercice comptent");
        check.Amount.Should().Be(1000m);
        check.Count.Should().Be(1);
    }

    [Fact]
    public void Un_chiffre_d_affaires_sans_tva_est_a_regarder_et_sans_chiffre_d_affaires_rien_n_est_mesure()
    {
        Check(Review([Sale(new(2026, 2, 1), Martin, 800m, 0m)]), ClosingReview.VatRate)
            .Should().BeEquivalentTo(new { Severity = ReviewSeverityDto.Info, Measure = 0m, Amount = 800m, Count = 1 });

        Check(Review(), ClosingReview.VatRate)
            .Should().BeEquivalentTo(new { Severity = ReviewSeverityDto.Ok, Measure = (decimal?)null, Amount = (decimal?)null, Count = 0 });
    }

    [Fact]
    public void Les_brouillons_d_avant_la_cloture_sont_a_regarder()
    {
        ReviewCheckResponse check = Check(Review(drafts: ["Martin", "Durand"]), ClosingReview.OldDrafts);

        check.Severity.Should().Be(ReviewSeverityDto.Info);
        check.Items.Select(i => i.Label).Should().Equal("Martin", "Durand");
        check.Items.Should().OnlyContain(i => i.Kind == ReviewItemKindDto.Invoice && i.Id == null);
        check.Amount.Should().BeNull();
    }

    [Fact]
    public void Les_controles_sont_ranges_du_plus_grave_au_moins_grave()
    {
        ClosingReviewResponse review = Review(
            delays: Delays(Unpaid("FAC-000009", 200, 10m)),
            drafts: ["Martin"]);

        review.Checks.Select(c => c.Severity).Should().BeInDescendingOrder();
        review.Checks[0].Code.Should().Be(ClosingReview.DoubtfulReceivables);
    }
}
