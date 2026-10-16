using Lettra.Contracts.Billing;
using Lettra.UI.Shared.Billing;

namespace Lettra.UI.Tests.Design;

public sealed class DashboardFiguresTests
{
    private static readonly DateOnly Aujourdhui = new(2026, 10, 16);

    private static InvoiceSummary Facture(string? numero, DateOnly? date, decimal ht,
        InvoiceKindDto kind = InvoiceKindDto.Invoice, InvoiceStatusDto status = InvoiceStatusDto.Validated) =>
        new(Guid.CreateVersion7(), kind, status, numero, Guid.Empty, "Dupont SARL", date, date?.AddDays(30), ht, ht * 1.2m);

    private static QuoteSummary Devis(string? numero, QuoteStatusDto status, decimal ht, bool expire = false) =>
        new(Guid.CreateVersion7(), numero, status, Guid.Empty, "Dupont SARL", numero is null ? null : new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 31), expire, ht, ht * 1.2m);

    [Fact]
    public void Le_chiffre_d_affaires_du_mois_deduit_les_avoirs_et_ignore_les_brouillons()
    {
        DashboardFigures chiffres = DashboardFigures.Compute(
        [
            Facture("FAC-2026-0010", new DateOnly(2026, 10, 2), 1000m),
            Facture("AV-2026-0001", new DateOnly(2026, 10, 9), 200m, InvoiceKindDto.CreditNote),
            Facture(null, null, 5000m, status: InvoiceStatusDto.Draft),
            Facture("FAC-2026-0005", new DateOnly(2026, 9, 30), 500m),
            Facture("FAC-2025-0099", new DateOnly(2026, 4, 30), 9999m),
        ], [], Aujourdhui);

        chiffres.Months.Select(m => m.Month).Should().Equal(
            new DateOnly(2026, 5, 1), new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1),
            new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1));
        chiffres.ThisMonth.Should().Be(800m);
        chiffres.PreviousMonth.Should().Be(500m);
        chiffres.Trend.Should().Be(60m);
        chiffres.InvoicesThisMonth.Should().Be(1, "un avoir n'est pas une facture du mois");
        chiffres.Months.Sum(m => m.ExclTax).Should().Be(1300m, "la facture d'avril est hors de la période de six mois");
    }

    [Fact]
    public void Sans_facture_le_mois_precedent_il_n_y_a_pas_d_evolution()
    {
        DashboardFigures chiffres = DashboardFigures.Compute([Facture("FAC-2026-0010", new DateOnly(2026, 10, 2), 1000m)], [], Aujourdhui);

        chiffres.Trend.Should().BeNull();
    }

    [Fact]
    public void Seuls_les_devis_valides_et_encore_valables_attendent_une_reponse()
    {
        DashboardFigures chiffres = DashboardFigures.Compute([],
        [
            Devis("DEV-2026-0001", QuoteStatusDto.Validated, 1000m),
            Devis("DEV-2026-0002", QuoteStatusDto.Validated, 300m),
            Devis("DEV-2026-0003", QuoteStatusDto.Validated, 7000m, expire: true),
            Devis("DEV-2026-0004", QuoteStatusDto.Accepted, 4000m),
            Devis(null, QuoteStatusDto.Draft, 9000m),
        ], Aujourdhui);

        chiffres.PendingQuotes.Should().Be(2);
        chiffres.PendingQuotesExclTax.Should().Be(1300m);
        chiffres.RecentQuotes.Should().HaveCount(4, "un brouillon n'a pas encore de numéro");
    }

    [Fact]
    public void Les_pieces_recentes_sont_les_cinq_dernieres_par_date_puis_par_numero()
    {
        DashboardFigures chiffres = DashboardFigures.Compute(
        [
            .. Enumerable.Range(1, 7).Select(n => Facture($"FAC-2026-{n:0000}", new DateOnly(2026, 10, 1 + (n / 2)), 100m)),
        ], [], Aujourdhui);

        chiffres.RecentInvoices.Select(i => i.Number).Should().Equal("FAC-2026-0007", "FAC-2026-0006", "FAC-2026-0005", "FAC-2026-0004", "FAC-2026-0003");
    }
}
