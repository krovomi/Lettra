using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Tax;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Exports.Application.Tax;

/// <summary>Une pièce validée ou émise du dossier, telle que le tableau des délais de paiement la lit.</summary>
public sealed record PaymentDelayDocument(
    Guid InvoiceId,
    InvoiceKind Kind,
    string Number,
    string CustomerName,
    bool BusinessCustomer,
    DateOnly IssueDate,
    DateOnly? DueDate,
    decimal TotalInclTax,
    Guid? CreditedInvoiceId,
    bool Disputed);

/// <summary>
/// Un encaissement d'une facture, daté : saisi dans Lettra (avec sa contre-passation éventuelle) ou seulement déclaré
/// à la plateforme agréée (<paramref name="Declared"/>).
/// </summary>
public sealed record PaymentDelaySettlement(Guid InvoiceId, DateOnly Date, decimal Amount, DateOnly? ReversalDate, bool Declared = false);

/// <summary>Lecture des pièces et des encaissements du dossier jusqu'à une date (lecture seule).</summary>
public interface IPaymentDelaysReader
{
    Task<IReadOnlyList<PaymentDelayDocument>> ReadDocumentsAsync(DateOnly to, CancellationToken cancellationToken);

    Task<IReadOnlyList<PaymentDelaySettlement>> ReadSettlementsAsync(DateOnly to, CancellationToken cancellationToken);
}

/// <summary>
/// Délais de paiement d'un exercice, côté factures émises : tableau du rapport de gestion à la clôture et factures dont
/// l'échéance dépasse le plafond légal. Sans date, la clôture du dernier exercice terminé.
/// </summary>
public sealed record GetPaymentDelaysQuery(DossierId Dossier, DateOnly? ClosingDate = null) : IQuery<Result<PaymentDelaysResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>
/// Calcul du tableau (art. D441-6, annexe 4-1). Une facture ou un acompte compte s'il est émis au plus tard à la clôture
/// et qu'il en reste dû ce jour-là : TTC moins les avoirs émis et les encaissements reçus (non contre-passés, saisis
/// ou déclarés à la plateforme) jusqu'à la clôture. « 0 jour » réunit les factures échues le jour même de la clôture ; les factures non encore échues sont
/// comptées à part. Une facture contestée sur la plateforme est exclue des tranches (partie B). Les pourcentages
/// rapportent les montants TTC au chiffre d'affaires TTC de l'exercice (factures et acomptes moins avoirs).
/// </summary>
public static class PaymentDelaysReport
{
    public static PaymentDelaysResponse Compute(
        IEnumerable<PaymentDelayDocument> documents,
        IEnumerable<PaymentDelaySettlement> settlements,
        DateOnly fiscalYearStart,
        DateOnly closingDate)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(settlements);
        PaymentDelayDocument[] issued = [.. documents.Where(d => d.IssueDate <= closingDate)];
        Dictionary<Guid, decimal> settled = Settled(issued, settlements, closingDate);

        decimal turnover = issued
            .Where(d => d.IssueDate >= fiscalYearStart)
            .Sum(d => d.Kind == InvoiceKind.CreditNote ? -d.TotalInclTax : d.TotalInclTax);

        List<PaymentDelayInvoiceResponse> unpaid = [];
        foreach (PaymentDelayDocument document in issued.Where(d => d is { Kind: not InvoiceKind.CreditNote, DueDate: not null }))
        {
            decimal outstanding = Math.Max(0m, document.TotalInclTax - settled.GetValueOrDefault(document.InvoiceId));
            if (outstanding <= 0m)
            {
                continue;
            }

            DateOnly due = document.DueDate!.Value;
            int daysLate = PaymentDelays.DaysLate(due, closingDate);
            int? bracket = daysLate >= 0 && !document.Disputed ? PaymentDelays.Bracket(daysLate) : null;
            unpaid.Add(new PaymentDelayInvoiceResponse(
                document.InvoiceId, document.Number, document.CustomerName, document.IssueDate, due, daysLate, bracket, outstanding, document.Disputed));
        }

        PaymentDelayBracketResponse[] brackets =
        [
            .. Enumerable.Range(0, PaymentDelays.BracketCount)
                .Select(b => Line(b, unpaid.Where(i => i.Bracket == b), turnover)),
        ];
        PaymentDelayInvoiceResponse[] excluded = [.. unpaid.Where(i => i.Disputed)];
        PaymentDelayInvoiceResponse[] notDue = [.. unpaid.Where(i => i is { Disputed: false, DaysLate: < 0 })];

        return new PaymentDelaysResponse(
            fiscalYearStart,
            closingDate,
            turnover,
            brackets,
            Line(-1, unpaid.Where(i => i.Bracket is >= 1), turnover),
            excluded.Length,
            excluded.Sum(i => i.OutstandingInclTax),
            ContractualDays(issued, fiscalYearStart),
            notDue.Length,
            notDue.Sum(i => i.OutstandingInclTax),
            [.. unpaid.OrderByDescending(i => i.DaysLate).ThenBy(i => i.Number, StringComparer.Ordinal)],
            [.. Breaches(issued, fiscalYearStart)]);
    }

    /// <summary>
    /// Avoirs émis et encaissements reçus jusqu'à la clôture, par facture. Comme pour le point des créances, on retient
    /// les encaissements saisis dans Lettra ou ceux déclarés à la plateforme s'ils sont plus élevés : un encaissement
    /// saisi est aussi déclaré.
    /// </summary>
    private static Dictionary<Guid, decimal> Settled(PaymentDelayDocument[] issued, IEnumerable<PaymentDelaySettlement> settlements, DateOnly closingDate)
    {
        Dictionary<Guid, decimal> settled = settlements
            .Where(s => s.Date <= closingDate && !(s.ReversalDate <= closingDate))
            .GroupBy(s => s.InvoiceId)
            .ToDictionary(g => g.Key, g => Math.Max(g.Where(s => !s.Declared).Sum(s => s.Amount), g.Where(s => s.Declared).Sum(s => s.Amount)));
        foreach (PaymentDelayDocument credit in issued.Where(d => d is { Kind: InvoiceKind.CreditNote, CreditedInvoiceId: not null }))
        {
            settled[credit.CreditedInvoiceId!.Value] = settled.GetValueOrDefault(credit.CreditedInvoiceId.Value) + credit.TotalInclTax;
        }

        return settled;
    }

    private static PaymentDelayBracketResponse Line(int bracket, IEnumerable<PaymentDelayInvoiceResponse> invoices, decimal turnover)
    {
        PaymentDelayInvoiceResponse[] all = [.. invoices];
        decimal amount = all.Sum(i => i.OutstandingInclTax);
        return new PaymentDelayBracketResponse(bracket, all.Length, amount, turnover > 0m ? decimal.Round(amount * 100m / turnover, 2) : null);
    }

    /// <summary>Délai de référence (partie C) : le délai le plus fréquent entre date de facture et échéance sur l'exercice.</summary>
    private static int? ContractualDays(PaymentDelayDocument[] issued, DateOnly fiscalYearStart) =>
        issued
            .Where(d => d is { Kind: not InvoiceKind.CreditNote, DueDate: not null } && d.IssueDate >= fiscalYearStart)
            .Select(d => d.DueDate!.Value.DayNumber - d.IssueDate.DayNumber)
            .GroupBy(days => days)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Select(g => (int?)g.Key)
            .FirstOrDefault();

    /// <summary>Factures de l'exercice à un professionnel dont l'échéance dépasse le plafond légal.</summary>
    private static IEnumerable<PaymentTermBreachResponse> Breaches(PaymentDelayDocument[] issued, DateOnly fiscalYearStart) =>
        issued
            .Where(d => d is { Kind: not InvoiceKind.CreditNote, BusinessCustomer: true, DueDate: not null } && d.IssueDate >= fiscalYearStart)
            .Where(d => PaymentDelays.DaysOverCeiling(d.IssueDate, d.DueDate!.Value) > 0)
            .Select(d => new PaymentTermBreachResponse(
                d.InvoiceId, d.Number, d.CustomerName, d.IssueDate, d.DueDate!.Value, PaymentDelays.LegalCeiling(d.IssueDate), PaymentDelays.DaysOverCeiling(d.IssueDate, d.DueDate.Value)))
            .OrderByDescending(b => b.DaysOver)
            .ThenBy(b => b.Number, StringComparer.Ordinal);
}

internal sealed class GetPaymentDelaysHandler(IPaymentDelaysReader reader, TaxDeclarations tax, TimeProvider clock)
    : IQueryHandler<GetPaymentDelaysQuery, Result<PaymentDelaysResponse>>
{
    public async Task<Result<PaymentDelaysResponse>> HandleAsync(GetPaymentDelaysQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        FiscalSettings settings = await tax.SettingsAsync(cancellationToken).ConfigureAwait(false);

        // Sans date : la clôture du dernier exercice terminé, celle dont on prépare le rapport de gestion.
        DateOnly closing = query.ClosingDate ?? settings.FiscalYearOf(LegalCalendar.Today(clock)).Start.AddDays(-1);
        // Une date en cours d'exercice donne le tableau à cette date, sur l'exercice commencé.
        DateOnly start = settings.FiscalYearOf(closing).Start;
        IReadOnlyList<PaymentDelayDocument> documents = await reader.ReadDocumentsAsync(closing, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<PaymentDelaySettlement> settlements = await reader.ReadSettlementsAsync(closing, cancellationToken).ConfigureAwait(false);
        return PaymentDelaysReport.Compute(documents, settlements, start, closing);
    }
}
