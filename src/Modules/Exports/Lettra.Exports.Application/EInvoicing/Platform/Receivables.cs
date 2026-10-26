using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Exports;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Domain.Platform;

namespace Lettra.Exports.Application.EInvoicing.Platform;

/// <summary>
/// Une pièce validée du dossier telle que le point des créances la lit, avec sa dernière transmission. La date
/// d'émission et le total hors taxes servent à expliquer le chiffre d'affaires d'un mois (« Pourquoi ce chiffre ? »).
/// </summary>
public sealed record ReceivableRow(
    Guid InvoiceId,
    InvoiceKind Kind,
    string Number,
    string CustomerName,
    DateOnly? DueDate,
    decimal TotalInclTax,
    decimal CreditedInclTax,
    PlatformTransmission? Transmission,
    decimal PaidInclTax = 0m,
    DateOnly? IssueDate = null,
    decimal TotalExclTax = 0m);

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
    private static readonly PlatformStatus[] AttentionStatuses =
        [PlatformStatus.Rejected, PlatformStatus.Refused, PlatformStatus.Disputed, PlatformStatus.Suspended];

    public static ReceivablesSummaryResponse Compute(IEnumerable<ReceivableRow> rows, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ReceivableRow[] all = [.. rows];
        OverdueInvoiceResponse[] overdue =
        [
            .. all
                .Where(r => IsOverdue(r, today))
                .Select(r => (Row: r, Outstanding: Outstanding(r)))
                .Select(r => new OverdueInvoiceResponse(
                    r.Row.InvoiceId, r.Row.Number, r.Row.CustomerName, r.Row.DueDate!.Value, today.DayNumber - r.Row.DueDate.Value.DayNumber, r.Outstanding))
                .OrderBy(o => o.DueDate)
                .ThenBy(o => o.Number, StringComparer.Ordinal),
        ];

        int notTransmitted = all.Count(r => r.Transmission is null);
        int attention = all.Count(r => r.Transmission is { } t && AttentionStatuses.Contains(t.Status));
        int collected = all.Count(r => r.Transmission is { Status: PlatformStatus.Collected, IsFinal: true });
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

    /// <summary>Ce qui est encaissé : le plus élevé de ce qui est saisi dans Lettra et de ce qui est déclaré à la plateforme.</summary>
    public static decimal Collected(ReceivableRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Math.Max(row.PaidInclTax, row.Transmission?.CollectedAmount ?? 0m);
    }

    /// <summary>Reste dû : TTC − avoirs − encaissé, jamais négatif.</summary>
    public static decimal Outstanding(ReceivableRow row) => Math.Max(0m, row.TotalInclTax - row.CreditedInclTax - Collected(row));

    /// <summary>Une facture ou un acompte échu la veille ou avant, dont il reste quelque chose à payer.</summary>
    public static bool IsOverdue(ReceivableRow row, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row is { Kind: not InvoiceKind.CreditNote, DueDate: { } due } && due < today && Outstanding(row) > 0m;
    }
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
