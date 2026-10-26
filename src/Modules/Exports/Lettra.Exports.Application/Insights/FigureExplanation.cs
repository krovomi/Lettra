using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.EInvoicing.Platform;

namespace Lettra.Exports.Application.Insights;

/// <summary>« Pourquoi ce chiffre ? » : la chaîne d'un chiffre de l'accueil jusqu'aux pièces.</summary>
/// <param name="Dossier">Le dossier courant.</param>
/// <param name="Figure">Le chiffre à expliquer.</param>
/// <param name="Month">Un jour du mois expliqué, pour le chiffre d'affaires d'un mois ; le mois en cours sinon.</param>
public sealed record ExplainFigureQuery(DossierId Dossier, ExplainedFigureDto Figure, DateOnly? Month = null)
    : IQuery<Result<FigureExplanationResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>
/// Les explications, pures et déterministes, avec les règles mêmes des écrans qu'elles expliquent : le chiffre
/// d'affaires d'un mois comme l'accueil (factures et acomptes émis dans le mois jusqu'à aujourd'hui, hors taxes, moins
/// les avoirs émis dans le mois) ; le montant en retard comme le point des créances (<see cref="ReceivablesSummary"/>).
/// </summary>
public static class FigureExplanations
{
    public static FigureExplanationResponse MonthRevenue(IEnumerable<ReceivableRow> rows, DateOnly month, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(rows);
        DateOnly first = new(month.Year, month.Month, 1);
        ExplanationLineResponse[] lines =
        [
            .. rows
                .Where(r => r.IssueDate is { } issued && issued.Year == first.Year && issued.Month == first.Month && issued <= today)
                .OrderBy(r => r.Kind == InvoiceKind.CreditNote)
                .ThenBy(r => r.IssueDate)
                .ThenBy(r => r.Number, StringComparer.Ordinal)
                .Select(r => r.Kind == InvoiceKind.CreditNote
                    ? new ExplanationLineResponse(r.InvoiceId, r.Number, r.CustomerName, ExplanationLineKindDto.CreditNote, r.IssueDate, -r.TotalExclTax)
                    : new ExplanationLineResponse(r.InvoiceId, r.Number, r.CustomerName, ExplanationLineKindDto.Invoice, r.IssueDate, r.TotalExclTax)),
        ];
        return new FigureExplanationResponse(ExplainedFigureDto.MonthRevenue, lines.Sum(l => l.Amount), today, first, lines);
    }

    public static FigureExplanationResponse Overdue(IEnumerable<ReceivableRow> rows, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ExplanationLineResponse[] lines =
        [
            .. rows
                .Where(r => ReceivablesSummary.IsOverdue(r, today))
                .OrderBy(r => r.DueDate)
                .ThenBy(r => r.Number, StringComparer.Ordinal)
                .Select(r => new ExplanationLineResponse(
                    r.InvoiceId,
                    r.Number,
                    r.CustomerName,
                    ExplanationLineKindDto.Invoice,
                    r.DueDate,
                    ReceivablesSummary.Outstanding(r),
                    r.TotalInclTax,
                    r.CreditedInclTax,
                    ReceivablesSummary.Collected(r),
                    today.DayNumber - r.DueDate!.Value.DayNumber)),
        ];
        return new FigureExplanationResponse(ExplainedFigureDto.Overdue, lines.Sum(l => l.Amount), today, null, lines);
    }
}

internal sealed class ExplainFigureHandler(IReceivablesReader reader, TimeProvider clock)
    : IQueryHandler<ExplainFigureQuery, Result<FigureExplanationResponse>>
{
    public async Task<Result<FigureExplanationResponse>> HandleAsync(ExplainFigureQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!Enum.IsDefined(query.Figure))
        {
            return Error.Validation(ErrorCodes.Validation.Invalid, nameof(query.Figure));
        }

        IReadOnlyList<ReceivableRow> rows = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        DateOnly today = LegalCalendar.Today(clock);
        return query.Figure == ExplainedFigureDto.MonthRevenue
            ? FigureExplanations.MonthRevenue(rows, query.Month ?? today, today)
            : FigureExplanations.Overdue(rows, today);
    }
}
