using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Calculations;
using Lettra.Contracts.Accounting;
using Lettra.Contracts.Tax;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Banking;
using Lettra.Exports.Application.Tax;

namespace Lettra.Exports.Application.Accounting;

/// <summary>
/// Révision assistée d'un exercice avant la clôture : contrôles automatiques sur les écritures, les créances et la
/// banque, chacun menant à ses pièces. Sans date, la clôture du dernier exercice terminé.
/// </summary>
public sealed record GetClosingReviewQuery(DossierId Dossier, DateOnly? ClosingDate = null) : IQuery<Result<ClosingReviewResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Ce que la révision lit du dossier, déjà calculé par les autres vues.</summary>
public sealed record ClosingReviewInput(
    IReadOnlyList<SalesJournalExportEntry> Entries,
    PaymentDelaysResponse Delays,
    BankReconciliationStatus Bank,
    IReadOnlyList<string> Drafts);

/// <summary>
/// Les contrôles de révision, en fonctions pures. Les comptes de bilan (411, 4191) se lisent depuis l'origine jusqu'à
/// la clôture ; les comptes de gestion (706, 707) et la TVA collectée, sur l'exercice. Les fournisseurs débiteurs
/// viendront avec les factures reçues.
/// </summary>
public static class ClosingReview
{
    public const string CustomersInCredit = "CustomersInCredit";
    public const string DownPaymentsUnapplied = "DownPaymentsUnapplied";
    public const string DoubtfulReceivables = "DoubtfulReceivables";
    public const string DisputedReceivables = "DisputedReceivables";
    public const string BankUnreconciled = "BankUnreconciled";
    public const string VatRate = "VatRate";
    public const string OldDrafts = "OldDrafts";

    /// <summary>Au-delà de six mois de retard à la clôture, une dépréciation de la créance est à envisager.</summary>
    public const int DoubtfulAfterDays = 180;

    /// <summary>Taux normal de TVA : un taux apparent plus élevé trahit une erreur de taux ou de compte.</summary>
    public const decimal StandardVatRate = 20m;

    private const decimal Tolerance = 0.005m;

    public static ClosingReviewResponse Compute(ClosingReviewInput input, DateOnly fiscalYearStart, DateOnly closingDate)
    {
        ArgumentNullException.ThrowIfNull(input);
        SalesJournalExportEntry[] entries = [.. input.Entries.Where(e => e.Date <= closingDate)];
        ReviewCheckResponse[] checks =
        [
            Balances(CustomersInCredit, entries, SalesAccounts.Customers, ReviewSeverityDto.Warning),
            Balances(DownPaymentsUnapplied, entries, SalesAccounts.DownPaymentsReceived, ReviewSeverityDto.Info),
            Receivables(DoubtfulReceivables, input.Delays.Invoices.Where(i => !i.Disputed && i.DaysLate > DoubtfulAfterDays)),
            Receivables(DisputedReceivables, input.Delays.Invoices.Where(i => i.Disputed)),
            Bank(input.Bank, closingDate),
            Vat([.. entries.Where(e => e.Date >= fiscalYearStart)]),
            Drafts(input.Drafts),
        ];
        return new ClosingReviewResponse(fiscalYearStart, closingDate, [.. checks.OrderByDescending(c => c.Severity)]);
    }

    /// <summary>
    /// Soldes anormaux d'un compte par client : un client créditeur (411 au crédit : trop-perçu, avoir non remboursé),
    /// ou un acompte reçu encore à imputer (4191 au crédit).
    /// </summary>
    private static ReviewCheckResponse Balances(string code, SalesJournalExportEntry[] entries, string account, ReviewSeverityDto severity)
    {
        ReviewItemResponse[] items =
        [
            .. entries
                .GroupBy(e => e.CustomerId)
                .Select(g => (Customer: g.Key, Name: g.Last().CustomerName, Credit: g.SelectMany(e => e.Lines).Where(l => l.Account == account).Sum(l => l.Credit - l.Debit)))
                .Where(c => c.Credit > Tolerance)
                .OrderByDescending(c => c.Credit)
                .Select(c => new ReviewItemResponse(ReviewItemKindDto.Customer, c.Customer, c.Name, c.Credit, null)),
        ];
        return Check(code, items.Length == 0 ? ReviewSeverityDto.Ok : severity, items);
    }

    private static ReviewCheckResponse Receivables(string code, IEnumerable<PaymentDelayInvoiceResponse> invoices)
    {
        ReviewItemResponse[] items =
        [
            .. invoices
                .OrderByDescending(i => i.DaysLate)
                .Select(i => new ReviewItemResponse(ReviewItemKindDto.Invoice, i.InvoiceId, $"{i.Number} · {i.CustomerName}", i.OutstandingInclTax, i.DueDate, i.DaysLate)),
        ];
        return Check(code, items.Length == 0 ? ReviewSeverityDto.Ok : ReviewSeverityDto.Warning, items);
    }

    /// <summary>Opérations du relevé encore à rapprocher : à traiter si la plus ancienne date d'avant la clôture.</summary>
    private static ReviewCheckResponse Bank(BankReconciliationStatus status, DateOnly closingDate)
    {
        if (status.PendingLines == 0)
        {
            return new ReviewCheckResponse(BankUnreconciled, ReviewSeverityDto.Ok, 0, null, null, []);
        }

        ReviewSeverityDto severity = status.OldestPendingDate is { } oldest && oldest <= closingDate ? ReviewSeverityDto.Warning : ReviewSeverityDto.Info;
        return new ReviewCheckResponse(
            BankUnreconciled, severity, status.PendingLines, null, null, [new ReviewItemResponse(ReviewItemKindDto.Bank, null, string.Empty, null, status.OldestPendingDate)]);
    }

    /// <summary>
    /// Taux apparent de TVA de l'exercice : TVA collectée (44571 et 44574) rapportée aux produits (706, 707). Au-dessus
    /// du taux normal, une ligne a un taux ou un compte faux ; des produits sans aucune TVA sont à expliquer.
    /// </summary>
    private static ReviewCheckResponse Vat(SalesJournalExportEntry[] entries)
    {
        AccountingEntryLine[] lines = [.. entries.SelectMany(e => e.Lines)];
        decimal revenue = lines.Where(l => l.Account is SalesAccounts.ServicesRevenue or SalesAccounts.GoodsRevenue).Sum(l => l.Credit - l.Debit);
        decimal vat = lines.Where(l => l.Account is SalesAccounts.VatCollected or SalesAccounts.VatPending).Sum(l => l.Credit - l.Debit);
        if (revenue <= Tolerance)
        {
            return new ReviewCheckResponse(VatRate, ReviewSeverityDto.Ok, 0, null, null, []);
        }

        decimal rate = decimal.Round(vat * 100m / revenue, 2);
        ReviewSeverityDto severity = rate > StandardVatRate + 0.01m ? ReviewSeverityDto.Warning
            : vat <= Tolerance ? ReviewSeverityDto.Info
            : ReviewSeverityDto.Ok;
        return new ReviewCheckResponse(VatRate, severity, severity == ReviewSeverityDto.Ok ? 0 : 1, revenue, rate, []);
    }

    /// <summary>Brouillons créés avant la clôture et jamais validés : une vente oubliée, ou un brouillon à supprimer.</summary>
    private static ReviewCheckResponse Drafts(IReadOnlyList<string> drafts) =>
        Check(OldDrafts, drafts.Count == 0 ? ReviewSeverityDto.Ok : ReviewSeverityDto.Info,
            [.. drafts.Select(name => new ReviewItemResponse(ReviewItemKindDto.Invoice, null, name, null, null))]);

    private static ReviewCheckResponse Check(string code, ReviewSeverityDto severity, ReviewItemResponse[] items) =>
        new(code, severity, items.Length, items.Any(i => i.Amount is not null) ? items.Sum(i => i.Amount ?? 0m) : null, null, items);
}

internal sealed class GetClosingReviewHandler(
    ISalesJournalReader journal,
    IPaymentDelaysReader delays,
    IBankStatementRepository statements,
    ITaxDocumentReader documents,
    TaxDeclarations tax,
    TimeProvider clock) : IQueryHandler<GetClosingReviewQuery, Result<ClosingReviewResponse>>
{
    public async Task<Result<ClosingReviewResponse>> HandleAsync(GetClosingReviewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        FiscalSettings settings = await tax.SettingsAsync(cancellationToken).ConfigureAwait(false);
        DateOnly closing = query.ClosingDate ?? settings.FiscalYearOf(LegalCalendar.Today(clock)).Start.AddDays(-1);
        DateOnly start = settings.FiscalYearOf(closing).Start;

        IReadOnlyList<SalesJournalExportEntry> entries = await journal.ReadAsync(DateOnly.MinValue, closing, cancellationToken).ConfigureAwait(false);
        PaymentDelaysResponse receivables = PaymentDelaysReport.Compute(
            await delays.ReadDocumentsAsync(closing, cancellationToken).ConfigureAwait(false),
            await delays.ReadSettlementsAsync(closing, cancellationToken).ConfigureAwait(false),
            start,
            closing);
        BankReconciliationStatus bank = await statements.StatusAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> drafts = await documents.ListDraftsAsync(new DateTimeOffset(closing.AddDays(1), TimeOnly.MinValue, TimeSpan.Zero), cancellationToken).ConfigureAwait(false);
        return ClosingReview.Compute(new ClosingReviewInput(entries, receivables, bank, drafts), start, closing);
    }
}
