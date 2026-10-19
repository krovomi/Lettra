using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Exports;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Exports.Application.EInvoicing.Platform;

/// <summary>Une pièce validée du dossier telle que le point des créances la lit, avec sa dernière transmission.</summary>
public sealed record ReceivableRow(
    Guid InvoiceId,
    InvoiceKind Kind,
    string Number,
    string CustomerName,
    DateOnly? DueDate,
    decimal TotalInclTax,
    decimal CreditedInclTax,
    PlatformTransmission? Transmission,
    decimal PaidInclTax = 0m);

/// <summary>Lecture des pièces validées ou émises du dossier et de leur dernière transmission (lecture seule).</summary>
public interface IReceivablesReader
{
    Task<IReadOnlyList<ReceivableRow>> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>Le point des créances pour le tableau de bord : factures en retard et état des transmissions.</summary>
public sealed record GetReceivablesSummaryQuery(DossierId Dossier) : IQuery<Result<ReceivablesSummaryResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>
/// Calcul du point des créances. Reste dû = TTC − avoirs validés − encaissements (ceux enregistrés dans Lettra, ou
/// ceux déclarés à la plateforme s'ils sont plus élevés : un encaissement saisi est aussi déclaré) ; une
/// facture ou un acompte est en retard le lendemain de son échéance tant qu'il reste dû. Un avoir n'est jamais en
/// retard mais compte dans l'état des transmissions.
/// </summary>
public static class ReceivablesSummary
{
    private static readonly PlatformStatusDto[] AttentionStatuses =
        [PlatformStatusDto.Rejected, PlatformStatusDto.Refused, PlatformStatusDto.Disputed, PlatformStatusDto.Suspended];

    public static ReceivablesSummaryResponse Compute(IEnumerable<ReceivableRow> rows, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ReceivableRow[] all = [.. rows];
        OverdueInvoiceResponse[] overdue =
        [
            .. all
                .Where(r => r is { Kind: not InvoiceKind.CreditNote, DueDate: { } due } && due < today)
                .Select(r => (Row: r, Outstanding: Outstanding(r)))
                .Where(r => r.Outstanding > 0m)
                .Select(r => new OverdueInvoiceResponse(
                    r.Row.InvoiceId, r.Row.Number, r.Row.CustomerName, r.Row.DueDate!.Value, today.DayNumber - r.Row.DueDate.Value.DayNumber, r.Outstanding))
                .OrderBy(o => o.DueDate)
                .ThenBy(o => o.Number, StringComparer.Ordinal),
        ];

        int notTransmitted = all.Count(r => r.Transmission is null);
        int attention = all.Count(r => r.Transmission is { } t && AttentionStatuses.Contains(t.Status));
        int collected = all.Count(r => r.Transmission is { Status: PlatformStatusDto.Collected, IsFinal: true });
        return new ReceivablesSummaryResponse(
            today,
            overdue.Length,
            overdue.Sum(o => o.OutstandingInclTax),
            [.. overdue.Take(ReceivablesSummaryResponse.OldestCount)],
            notTransmitted,
            all.Length - notTransmitted - attention - collected,
            attention,
            collected);
    }

    private static decimal Outstanding(ReceivableRow row) =>
        Math.Max(0m, row.TotalInclTax - row.CreditedInclTax - Math.Max(row.PaidInclTax, row.Transmission?.CollectedAmount ?? 0m));
}

internal sealed class GetReceivablesSummaryHandler(IReceivablesReader reader, TimeProvider clock)
    : IQueryHandler<GetReceivablesSummaryQuery, Result<ReceivablesSummaryResponse>>
{
    public async Task<Result<ReceivablesSummaryResponse>> HandleAsync(GetReceivablesSummaryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyList<ReceivableRow> rows = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return ReceivablesSummary.Compute(rows, LegalCalendar.Today(clock));
    }
}
