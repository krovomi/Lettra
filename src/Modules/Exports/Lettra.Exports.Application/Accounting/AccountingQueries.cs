using System.Globalization;
using System.Text;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features;
using Lettra.Calculations;
using Lettra.Contracts.Accounting;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Exports.Application.Accounting;

/// <summary>
/// Consulter le journal des ventes d'une période, du plus récent au plus ancien, filtré sur le numéro de pièce ou le
/// client (sans accents ni casse) et sur un compte. Les totaux portent sur toutes les écritures retenues, pas sur la page.
/// </summary>
public sealed record GetSalesJournalQuery(
    DossierId Dossier,
    DateOnly From,
    DateOnly To,
    string? Search = null,
    string? Account = null,
    Guid? CustomerId = null,
    int Page = 1,
    int PageSize = 50) : IQuery<Result<SalesJournalResponse>>, IRequirePermission
{
    public const int MaxPageSize = Paging.MaxPageSize;

    public const int MaxSearchLength = 100;

    public Permission Permission => Permission.DossierLire;
}

/// <summary>Consulter la balance des comptes d'une période et la balance auxiliaire des clients.</summary>
public sealed record GetTrialBalanceQuery(DossierId Dossier, DateOnly From, DateOnly To)
    : IQuery<Result<TrialBalanceResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Consulter le grand livre d'un compte sur une période, éventuellement pour un seul client.</summary>
public sealed record GetAccountLedgerQuery(DossierId Dossier, string Account, DateOnly From, DateOnly To, Guid? CustomerId = null)
    : IQuery<Result<AccountLedgerResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Refus d'une consultation de la comptabilité.</summary>
public static class AccountingErrors
{
    public const string PeriodInvalidCode = ErrorCodes.Accounting.PeriodInvalid;
    public const string PeriodTooLongCode = ErrorCodes.Accounting.PeriodTooLong;
    public const string AccountInvalidCode = ErrorCodes.Accounting.AccountInvalid;

    public static Error PeriodInvalid => Error.Validation(PeriodInvalidCode, "To");

    public static Error PeriodTooLong => Error.Validation(PeriodTooLongCode, "To");

    public static Error AccountInvalid => Error.Validation(AccountInvalidCode, "Account");
}

/// <summary>
/// Vues de consultation calculées à partir des écritures des journaux de ventes et de banque, comme le FEC : rien n'est
/// recalculé, les lignes sont celles enregistrées à la validation des pièces et à l'enregistrement des encaissements. Fonctions pures, testées sans base.
/// </summary>
public static class AccountingViews
{
    /// <summary>Une consultation couvre au plus cinq ans : au-delà, l'écran demande de restreindre la période.</summary>
    public const int MaxPeriodMonths = 60;

    public static Result ValidatePeriod(DateOnly from, DateOnly to) =>
        to < from ? AccountingErrors.PeriodInvalid
        : to >= from.AddMonths(MaxPeriodMonths) ? AccountingErrors.PeriodTooLong
        : Result.Success();

    /// <summary>Un numéro de compte du plan comptable : 3 à 20 chiffres, classe 1 à 8 (même règle que le validateur FEC).</summary>
    public static bool IsAccountNumber(string? account) =>
        account is { Length: >= 3 and <= 20 } && account.All(char.IsAsciiDigit) && account[0] is >= '1' and <= '8';

    public static SalesJournalResponse Journal(
        IReadOnlyList<SalesJournalExportEntry> entries, DateOnly from, DateOnly to, string? search, string? account, Guid? customerId, int page, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(entries);
        string? needle = ListSearch.Normalize(search) is { } text ? Fold(text) : null;
        List<SalesJournalExportEntry> kept = entries
            .Where(e => customerId is null || e.CustomerId == customerId)
            .Where(e => account is null || e.Lines.Any(l => l.Account == account))
            .Where(e => needle is null || Fold(e.PieceNumber).Contains(needle, StringComparison.Ordinal) || Fold(e.CustomerName).Contains(needle, StringComparison.Ordinal))
            .ToList();

        // Le plus récent d'abord : on consulte d'habitude les dernières pièces. L'ordre de validation départage un même jour.
        List<JournalEntryResponse> items = Enumerable.Reverse(kept)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToResponse)
            .ToList();
        IEnumerable<AccountingEntryLine> lines = kept.SelectMany(e => e.Lines);
        return new SalesJournalResponse(from, to, items, page, pageSize, kept.Count, lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
    }

    public static TrialBalanceResponse Balance(IReadOnlyList<SalesJournalExportEntry> entries, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(entries);
        List<AccountBalanceResponse> accounts = entries
            .SelectMany(e => e.Lines)
            .GroupBy(l => l.Account, StringComparer.Ordinal)
            .Select(g => new AccountBalanceResponse(g.Key, g.Sum(l => l.Debit), g.Sum(l => l.Credit), g.Count()))
            .OrderBy(a => a.Account, StringComparer.Ordinal)
            .ToList();

        List<CustomerBalanceResponse> customers = entries
            .GroupBy(e => e.CustomerId)
            .Select(g =>
            {
                List<AccountingEntryLine> receivable = g.SelectMany(e => e.Lines).Where(l => l.Account == SalesAccounts.Customers).ToList();
                return new CustomerBalanceResponse(g.Key, g.Last().CustomerName, receivable.Sum(l => l.Debit), receivable.Sum(l => l.Credit), g.Count());
            })
            .OrderByDescending(c => c.Balance)
            .ThenBy(c => c.CustomerName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new TrialBalanceResponse(from, to, accounts, customers, accounts.Sum(a => a.Debit), accounts.Sum(a => a.Credit));
    }

    /// <summary>
    /// Grand livre : <paramref name="before"/> donne le solde d'ouverture, <paramref name="period"/> les lignes, dans
    /// l'ordre chronologique avec le solde cumulé.
    /// </summary>
    public static AccountLedgerResponse Ledger(
        string account, Guid? customerId, IReadOnlyList<SalesJournalExportEntry> before, IReadOnlyList<SalesJournalExportEntry> period, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(period);
        bool Kept(SalesJournalExportEntry e) => customerId is null || e.CustomerId == customerId;

        decimal opening = before.Where(Kept).SelectMany(e => e.Lines).Where(l => l.Account == account).Sum(l => l.Debit - l.Credit);
        decimal running = opening;
        List<LedgerLineResponse> lines = [];
        foreach (SalesJournalExportEntry entry in period.Where(Kept))
        {
            foreach (AccountingEntryLine line in entry.Lines.Where(l => l.Account == account))
            {
                running += line.Debit - line.Credit;
                lines.Add(new LedgerLineResponse(
                    entry.Date, entry.InvoiceId, (InvoiceKindDto)(int)entry.Kind, entry.PieceNumber, entry.CustomerId, entry.CustomerName,
                    line.Debit, line.Credit, running, entry.JournalCode, line.Account == SalesAccounts.Customers ? entry.LetteringCode : null));
            }
        }

        return new AccountLedgerResponse(account, customerId, from, to, opening, lines, lines.Sum(l => l.Debit), lines.Sum(l => l.Credit), running);
    }

    private static JournalEntryResponse ToResponse(SalesJournalExportEntry entry) => new(
        entry.InvoiceId,
        (InvoiceKindDto)(int)entry.Kind,
        entry.PieceNumber,
        entry.Date,
        entry.CustomerId,
        entry.CustomerName,
        entry.Lines.Sum(l => l.Debit),
        entry.Lines.Select(l => new JournalLineResponse(l.Account, l.Debit, l.Credit, l.VatRatePercent)).ToList(),
        entry.JournalCode,
        entry.LetteringCode,
        entry.IsReversal);

    /// <summary>Majuscules sans accents : « Société » et « societe » se trouvent l'une l'autre.</summary>
    private static string Fold(string text)
    {
        string decomposed = text.Normalize(NormalizationForm.FormD);
        StringBuilder folded = new(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                folded.Append(char.ToUpperInvariant(c));
            }
        }

        return folded.ToString();
    }
}

internal sealed class GetSalesJournalHandler(ISalesJournalReader journal) : IQueryHandler<GetSalesJournalQuery, Result<SalesJournalResponse>>
{
    public async Task<Result<SalesJournalResponse>> HandleAsync(GetSalesJournalQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        List<Error> errors = [.. AccountingViews.ValidatePeriod(query.From, query.To).Errors, .. Paging.Validate(query.Page, query.PageSize)];

        if (query.Search is { Length: > GetSalesJournalQuery.MaxSearchLength })
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.TooLong, nameof(query.Search)));
        }

        if (query.Account is not null && !AccountingViews.IsAccountNumber(query.Account))
        {
            errors.Add(AccountingErrors.AccountInvalid);
        }

        if (errors.Count > 0)
        {
            return Result<SalesJournalResponse>.Failure(errors);
        }

        IReadOnlyList<SalesJournalExportEntry> entries = await journal.ReadAsync(query.From, query.To, cancellationToken).ConfigureAwait(false);
        return AccountingViews.Journal(entries, query.From, query.To, query.Search, query.Account, query.CustomerId, query.Page, query.PageSize);
    }
}

internal sealed class GetTrialBalanceHandler(ISalesJournalReader journal) : IQueryHandler<GetTrialBalanceQuery, Result<TrialBalanceResponse>>
{
    public async Task<Result<TrialBalanceResponse>> HandleAsync(GetTrialBalanceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Result period = AccountingViews.ValidatePeriod(query.From, query.To);
        if (period.IsFailure)
        {
            return Result<TrialBalanceResponse>.Failure(period.Errors);
        }

        IReadOnlyList<SalesJournalExportEntry> entries = await journal.ReadAsync(query.From, query.To, cancellationToken).ConfigureAwait(false);
        return AccountingViews.Balance(entries, query.From, query.To);
    }
}

internal sealed class GetAccountLedgerHandler(ISalesJournalReader journal) : IQueryHandler<GetAccountLedgerQuery, Result<AccountLedgerResponse>>
{
    public async Task<Result<AccountLedgerResponse>> HandleAsync(GetAccountLedgerQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        List<Error> errors = [.. AccountingViews.ValidatePeriod(query.From, query.To).Errors];
        if (!AccountingViews.IsAccountNumber(query.Account))
        {
            errors.Add(AccountingErrors.AccountInvalid);
        }

        if (errors.Count > 0)
        {
            return Result<AccountLedgerResponse>.Failure(errors);
        }

        // Solde d'ouverture : tous les mouvements antérieurs. Le journal des ventes d'un dossier reste petit (lecture en mémoire, comme le FEC).
        IReadOnlyList<SalesJournalExportEntry> before = query.From == DateOnly.MinValue
            ? []
            : await journal.ReadAsync(DateOnly.MinValue, query.From.AddDays(-1), cancellationToken).ConfigureAwait(false);
        IReadOnlyList<SalesJournalExportEntry> period = await journal.ReadAsync(query.From, query.To, cancellationToken).ConfigureAwait(false);
        return AccountingViews.Ledger(query.Account, query.CustomerId, before, period, query.From, query.To);
    }
}
