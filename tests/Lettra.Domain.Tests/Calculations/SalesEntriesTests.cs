using Lettra.Calculations;

namespace Lettra.Domain.Tests.Calculations;

public sealed class SalesEntriesTests
{
    private static DocumentTotals Totals(params LineInput[] lines) => DocumentCalculator.Compute(lines);

    [Fact]
    public void Une_facture_debite_le_client_et_credite_ventes_et_tva_par_taux()
    {
        DocumentTotals totals = Totals(new LineInput(4m, 100m, 20m), new LineInput(2m, 19.99m, 5.5m));

        IReadOnlyList<AccountingEntryLine> entry = SalesEntryCalculator.Compute(
            AccountingDocumentKind.Invoice,
            [new RevenueLine(RevenueNature.Services, 400m), new RevenueLine(RevenueNature.Goods, 39.98m)],
            totals);

        entry.Should().ContainEquivalentOf(new AccountingEntryLine(SalesAccounts.Customers, totals.TotalInclTax, 0m));
        entry.Should().ContainEquivalentOf(new AccountingEntryLine(SalesAccounts.ServicesRevenue, 0m, 400m));
        entry.Should().ContainEquivalentOf(new AccountingEntryLine(SalesAccounts.GoodsRevenue, 0m, 39.98m));
        entry.Should().ContainEquivalentOf(new AccountingEntryLine(SalesAccounts.VatCollected, 0m, 80m, 20m));
        entry.Should().ContainEquivalentOf(new AccountingEntryLine(SalesAccounts.VatCollected, 0m, 2.20m, 5.5m));
        entry.Sum(l => l.Debit).Should().Be(entry.Sum(l => l.Credit));
    }

    [Fact]
    public void Un_avoir_inverse_les_sens()
    {
        DocumentTotals totals = Totals(new LineInput(1m, 100m, 20m));

        IReadOnlyList<AccountingEntryLine> entry = SalesEntryCalculator.Compute(
            AccountingDocumentKind.CreditNote, [new RevenueLine(RevenueNature.Services, 100m)], totals);

        entry.Should().ContainEquivalentOf(new AccountingEntryLine(SalesAccounts.Customers, 0m, 120m));
        entry.Should().ContainEquivalentOf(new AccountingEntryLine(SalesAccounts.ServicesRevenue, 100m, 0m));
        entry.Should().ContainEquivalentOf(new AccountingEntryLine(SalesAccounts.VatCollected, 20m, 0m, 20m));
    }

    [Fact]
    public void La_deduction_d_un_acompte_solde_le_compte_4191()
    {
        DocumentTotals totals = Totals(new LineInput(1m, 1000m, 20m), new LineInput(-1m, 300m, 20m));

        IReadOnlyList<AccountingEntryLine> entry = SalesEntryCalculator.Compute(
            AccountingDocumentKind.Invoice,
            [new RevenueLine(RevenueNature.Services, 1000m), new RevenueLine(RevenueNature.DownPayment, -300m)],
            totals);

        entry.Should().ContainEquivalentOf(new AccountingEntryLine(SalesAccounts.DownPaymentsReceived, 300m, 0m));
        entry.Should().ContainEquivalentOf(new AccountingEntryLine(SalesAccounts.Customers, 840m, 0m));
        entry.Sum(l => l.Debit).Should().Be(entry.Sum(l => l.Credit));
    }

    [Fact]
    public void Le_recapitulatif_de_tva_separe_les_categories_a_taux_nul()
    {
        DocumentTotals totals = Totals(
            new LineInput(1m, 100m, 20m),
            new LineInput(1m, 50m, 0m, VatCategory: VatCategoryCodes.Exempt),
            new LineInput(1m, 30m, 0m, VatCategory: VatCategoryCodes.ZeroRated));

        totals.Breakdown.Select(b => (b.VatCategory, b.VatRatePercent)).Should().Equal(
            (VatCategoryCodes.Standard, 20m), (VatCategoryCodes.Exempt, 0m), (VatCategoryCodes.ZeroRated, 0m));
        totals.TotalInclTax.Should().Be(200m);
    }
}
