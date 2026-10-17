using Lettra.Contracts.Exports;
using Lettra.Domain.Billing;
using Lettra.Exports.Application.EInvoicing.Platform;

namespace Lettra.Exports.Tests.Platform;

/// <summary>Point des créances du tableau de bord (vague 5, lane E) : factures en retard et état des transmissions.</summary>
public sealed class ReceivablesSummaryTests
{
    private static readonly DateOnly Aujourdhui = new(2026, 10, 16);
    private static readonly DateTimeOffset Depot = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    private static ReceivableRow Piece(
        string numero, DateOnly? echeance, decimal ttc, PlatformTransmission? transmission = null,
        InvoiceKind kind = InvoiceKind.Invoice, decimal avoirs = 0m, string client = "Dupont SARL") =>
        new(Guid.CreateVersion7(), kind, numero, client, echeance, ttc, avoirs, transmission);

    private static PlatformTransmission Transmission(decimal ttc, params PlatformStatusEvent[] suite) => new(
        Guid.CreateVersion7(), Guid.CreateVersion7(), "FAC", PlatformChannelDto.EInvoice, EInvoiceFormatDto.Cii, "Simulée", "REF", ttc, Depot,
        [new PlatformStatusEvent(PlatformStatusDto.Deposited, Depot), .. suite]);

    private static PlatformStatusEvent Statut(PlatformStatusDto statut, decimal? montant = null) => new(statut, Depot.AddDays(5), Amount: montant);

    [Fact]
    public void Une_facture_est_en_retard_le_lendemain_de_son_echeance_pour_son_reste_du()
    {
        ReceivablesSummaryResponse point = ReceivablesSummary.Compute(
        [
            Piece("FAC-2026-0001", Aujourdhui.AddDays(-1), 1200m, Transmission(1200m, Statut(PlatformStatusDto.Collected, 200m))),
            Piece("FAC-2026-0002", Aujourdhui, 600m),
            Piece("FAC-2026-0003", Aujourdhui.AddDays(-40), 360m, avoirs: 60m),
            Piece("FAC-2026-0004", null, 900m),
        ], Aujourdhui);

        point.Today.Should().Be(Aujourdhui);
        point.OverdueCount.Should().Be(2, "l'échéance du jour n'est pas encore dépassée et une pièce sans échéance n'est jamais en retard");
        point.OverdueInclTax.Should().Be(1000m + 300m);
        point.Oldest.Select(o => (o.Number, o.DaysLate, o.OutstandingInclTax)).Should().Equal(
            ("FAC-2026-0003", 40, 300m), ("FAC-2026-0001", 1, 1000m));
    }

    [Fact]
    public void Une_facture_soldee_par_encaissement_ou_avoir_et_un_avoir_ne_sont_jamais_en_retard()
    {
        ReceivablesSummaryResponse point = ReceivablesSummary.Compute(
        [
            Piece("FAC-2026-0001", Aujourdhui.AddDays(-10), 360m, Transmission(360m, Statut(PlatformStatusDto.Collected, 360m))),
            Piece("FAC-2026-0002", Aujourdhui.AddDays(-10), 360m, avoirs: 360m),
            Piece("FAC-2026-0003", Aujourdhui.AddDays(-10), 360m, avoirs: 500m),
            Piece("AV-2026-0001", Aujourdhui.AddDays(-10), 360m, kind: InvoiceKind.CreditNote),
        ], Aujourdhui);

        point.OverdueCount.Should().Be(0);
        point.OverdueInclTax.Should().Be(0m);
        point.Oldest.Should().BeEmpty();
    }

    [Fact]
    public void Seules_les_cinq_plus_anciennes_sont_detaillees_mais_toutes_sont_comptees()
    {
        ReceivablesSummaryResponse point = ReceivablesSummary.Compute(
            [.. Enumerable.Range(1, 8).Select(n => Piece($"FAC-2026-{n:0000}", Aujourdhui.AddDays(-n), 100m))], Aujourdhui);

        point.OverdueCount.Should().Be(8);
        point.OverdueInclTax.Should().Be(800m);
        point.Oldest.Select(o => o.Number).Should().Equal("FAC-2026-0008", "FAC-2026-0007", "FAC-2026-0006", "FAC-2026-0005", "FAC-2026-0004");
    }

    [Fact]
    public void Chaque_piece_compte_une_fois_selon_sa_derniere_transmission()
    {
        ReceivablesSummaryResponse point = ReceivablesSummary.Compute(
        [
            Piece("FAC-2026-0001", null, 100m),
            Piece("AV-2026-0001", null, 100m, kind: InvoiceKind.CreditNote),
            Piece("FAC-2026-0002", null, 100m, Transmission(100m)),
            Piece("FAC-2026-0003", null, 100m, Transmission(100m, Statut(PlatformStatusDto.MadeAvailable))),
            Piece("FAC-2026-0004", null, 100m, Transmission(100m, Statut(PlatformStatusDto.Collected, 40m))),
            Piece("FAC-2026-0005", null, 100m, Transmission(100m, Statut(PlatformStatusDto.Rejected))),
            Piece("FAC-2026-0006", null, 100m, Transmission(100m, Statut(PlatformStatusDto.MadeAvailable), Statut(PlatformStatusDto.Disputed))),
            Piece("FAC-2026-0007", null, 100m, Transmission(100m, Statut(PlatformStatusDto.Collected, 100m))),
        ], Aujourdhui);

        point.NotTransmitted.Should().Be(2);
        point.InProgress.Should().Be(3, "déposée, mise à disposition et encaissée en partie");
        point.NeedsAttention.Should().Be(2, "rejetée et en litige");
        point.Collected.Should().Be(1);
    }
}
