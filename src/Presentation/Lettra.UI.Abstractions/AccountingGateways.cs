using Lettra.Contracts.Accounting;
using Lettra.Contracts.Common;

namespace Lettra.UI.Abstractions;

/// <summary>Filtre du journal des ventes : période (bornes comprises), recherche sur la pièce ou le client, compte, client.</summary>
public sealed record JournalQuery(
    DateOnly From,
    DateOnly To,
    string? Search = null,
    string? Account = null,
    Guid? CustomerId = null,
    int Page = 1,
    int PageSize = 50);

/// <summary>
/// Pages Comptabilité : consultation du journal des ventes, de la balance et du grand livre d'un compte (lecture seule,
/// permission <c>DossierLire</c>). Refus possibles : <c>Accounting.PeriodInvalid</c>, <c>Accounting.PeriodTooLong</c>
/// (cinq ans au plus), <c>Accounting.AccountInvalid</c>.
/// </summary>
public interface IAccountingGateway
{
    Task<OperationResult<SalesJournalResponse>> GetJournalAsync(JournalQuery query, CancellationToken cancellationToken);

    Task<OperationResult<TrialBalanceResponse>> GetBalanceAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Grand livre d'un compte ; avec <paramref name="customerId"/>, le compte client (411) de ce seul client.</summary>
    Task<OperationResult<AccountLedgerResponse>> GetLedgerAsync(string account, DateOnly from, DateOnly to, Guid? customerId, CancellationToken cancellationToken);
}
