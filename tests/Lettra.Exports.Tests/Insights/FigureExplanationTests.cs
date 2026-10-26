using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Exports.Application.Insights;

namespace Lettra.Exports.Tests.Insights;

/// <summary>
/// « Pourquoi ce chiffre ? » : chaque explication retrouve, pièce par pièce, le chiffre de l'accueil qu'elle explique,
/// avec ses règles (mois d'émission jusqu'à aujourd'hui, avoirs retranchés, encaissé le plus grand des deux).
/// </summary>
public sealed class FigureExplanationTests
{
    private static readonly DateOnly Today = new(2026, 10, 26);

    private static ReceivableRow Row(
        string number, InvoiceKind kind, DateOnly issued, decimal ht, DateOnly? due = null, decimal credited = 0m, decimal paid = 0m) =>
        new(Guid.CreateVersion7(), kind, number, "Client " + number, due, ht * 1.2m, credited, null, paid, issued, ht);

    [Fact]
    public void Le_chiffre_d_affaires_du_mois_compte_les_factures_emises_jusqu_a_aujourd_hui_moins_les_avoirs()
    {
        ReceivableRow[] rows =
        [
            Row("FA-3", InvoiceKind.Invoice, new DateOnly(2026, 10, 20), 300m),
            Row("AV-1", InvoiceKind.CreditNote, new DateOnly(2026, 10, 5), 50m),
            Row("FA-1", InvoiceKind.Invoice, new DateOnly(2026, 10, 1), 100m),
            Row("AC-1", InvoiceKind.DownPayment, new DateOnly(2026, 10, 2), 200m),
            Row("FA-9", InvoiceKind.Invoice, new DateOnly(2026, 9, 30), 1000m),
            Row("FA-X", InvoiceKind.Invoice, new DateOnly(2026, 10, 27), 1000m),
        ];

        FigureExplanationResponse why = FigureExplanations.MonthRevenue(rows, new DateOnly(2026, 10, 15), Today);

        why.Figure.Should().Be(ExplainedFigureDto.MonthRevenue);
        why.Month.Should().Be(new DateOnly(2026, 10, 1));
        why.Value.Should().Be(550m);
        why.Lines.Select(l => l.Number).Should().Equal("FA-1", "AC-1", "FA-3", "AV-1");
        why.Lines[^1].Kind.Should().Be(ExplanationLineKindDto.CreditNote);
        why.Lines[^1].Amount.Should().Be(-50m);
    }

    [Fact]
    public void Le_montant_en_retard_deroule_total_avoirs_et_encaisse_de_chaque_facture_echue()
    {
        ReceivableRow late = Row("FA-1", InvoiceKind.Invoice, new DateOnly(2026, 8, 1), 1000m, due: new DateOnly(2026, 9, 1), credited: 100m, paid: 200m);
        ReceivableRow[] rows =
        [
            Row("FA-2", InvoiceKind.Invoice, new DateOnly(2026, 9, 1), 100m, due: new DateOnly(2026, 10, 25)),
            late,
            Row("FA-3", InvoiceKind.Invoice, new DateOnly(2026, 9, 1), 100m, due: new DateOnly(2026, 9, 2), paid: 120m),
            Row("FA-4", InvoiceKind.Invoice, new DateOnly(2026, 10, 1), 100m, due: Today),
            Row("AV-1", InvoiceKind.CreditNote, new DateOnly(2026, 9, 1), 100m, due: new DateOnly(2026, 9, 1)),
        ];

        FigureExplanationResponse why = FigureExplanations.Overdue(rows, Today);

        why.Value.Should().Be(ReceivablesSummary.Compute(rows, Today).OverdueInclTax).And.Be(900m + 120m);
        why.Month.Should().BeNull();
        why.Lines.Select(l => l.Number).Should().Equal("FA-1", "FA-2");
        ExplanationLineResponse first = why.Lines[0];
        (first.TotalInclTax, first.CreditedInclTax, first.CollectedInclTax, first.Amount, first.DaysLate)
            .Should().Be((1200m, 100m, 200m, 900m, 55));
    }

    [Fact]
    public async Task Le_handler_explique_le_mois_en_cours_par_defaut_et_refuse_un_chiffre_inconnu()
    {
        ReceivableRow[] rows = [Row("FA-1", InvoiceKind.Invoice, Today, 100m, due: Today.AddDays(-1))];
        ExplainFigureHandler handler = new(new Reader(rows), new Clock(new DateTimeOffset(2026, 10, 26, 22, 30, 0, TimeSpan.Zero)));
        DossierId dossier = DossierId.From(Guid.CreateVersion7());

        Result<FigureExplanationResponse> revenue = await handler.HandleAsync(new ExplainFigureQuery(dossier, ExplainedFigureDto.MonthRevenue), default);
        Result<FigureExplanationResponse> overdue = await handler.HandleAsync(new ExplainFigureQuery(dossier, ExplainedFigureDto.Overdue), default);
        Result<FigureExplanationResponse> unknown = await handler.HandleAsync(new ExplainFigureQuery(dossier, (ExplainedFigureDto)42), default);

        revenue.Value.Today.Should().Be(new DateOnly(2026, 10, 26), "22 h 30 UTC est encore le 26 à Paris");
        revenue.Value.Value.Should().Be(100m);
        overdue.Value.Value.Should().Be(120m);
        unknown.IsFailure.Should().BeTrue();
        unknown.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Validation.Invalid);
    }

    private sealed class Reader(IReadOnlyList<ReceivableRow> rows) : IReceivablesReader
    {
        public Task<IReadOnlyList<ReceivableRow>> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(rows);
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
