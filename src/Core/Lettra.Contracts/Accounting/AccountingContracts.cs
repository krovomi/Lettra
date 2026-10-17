using Lettra.Contracts.Billing;

namespace Lettra.Contracts.Accounting;

/// <summary>
/// Une ligne d'écriture comptable : un compte du plan comptable général, au débit ou au crédit. Le taux de TVA est porté
/// par les lignes de TVA collectée (44571) et de produits, pour les distinguer à l'écran.
/// </summary>
public sealed record JournalLineResponse(string Account, decimal Debit, decimal Credit, decimal? VatRatePercent);

/// <summary>
/// Une écriture du journal des ventes : celle d'une pièce validée (facture, acompte ou avoir), toujours équilibrée.
/// <see cref="Amount"/> est le total des débits (le TTC de la pièce).
/// </summary>
public sealed record JournalEntryResponse(
    Guid InvoiceId,
    InvoiceKindDto Kind,
    string PieceNumber,
    DateOnly Date,
    Guid CustomerId,
    string CustomerName,
    decimal Amount,
    IReadOnlyList<JournalLineResponse> Lines);

/// <summary>Une page du journal des ventes sur une période, avec les totaux de toutes les écritures retenues par le filtre.</summary>
public sealed record SalesJournalResponse(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<JournalEntryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    decimal TotalDebit,
    decimal TotalCredit);

/// <summary>Un compte de la balance : ses mouvements de la période et son solde (débiteur si positif).</summary>
public sealed record AccountBalanceResponse(string Account, decimal Debit, decimal Credit, int LineCount)
{
    public decimal Balance => Debit - Credit;
}

/// <summary>Un client dans la balance auxiliaire du compte 411 : ce qui lui a été facturé, moins ses avoirs.</summary>
public sealed record CustomerBalanceResponse(Guid CustomerId, string CustomerName, decimal Debit, decimal Credit, int EntryCount)
{
    public decimal Balance => Debit - Credit;
}

/// <summary>
/// Balance de la période : comptes généraux par numéro, puis balance auxiliaire des clients (compte 411 détaillé par
/// client, du solde le plus fort au plus faible). Les totaux débit et crédit sont égaux : chaque écriture est équilibrée.
/// </summary>
public sealed record TrialBalanceResponse(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<AccountBalanceResponse> Accounts,
    IReadOnlyList<CustomerBalanceResponse> Customers,
    decimal TotalDebit,
    decimal TotalCredit);

/// <summary>Une ligne du grand livre d'un compte, avec le solde cumulé après elle.</summary>
public sealed record LedgerLineResponse(
    DateOnly Date,
    Guid InvoiceId,
    InvoiceKindDto Kind,
    string PieceNumber,
    Guid CustomerId,
    string CustomerName,
    decimal Debit,
    decimal Credit,
    decimal RunningBalance);

/// <summary>
/// Grand livre d'un compte sur une période, éventuellement limité à un client (compte 411 auxiliaire). Le solde
/// d'ouverture cumule tous les mouvements antérieurs à <see cref="From"/>.
/// </summary>
public sealed record AccountLedgerResponse(
    string Account,
    Guid? CustomerId,
    DateOnly From,
    DateOnly To,
    decimal OpeningBalance,
    IReadOnlyList<LedgerLineResponse> Lines,
    decimal TotalDebit,
    decimal TotalCredit,
    decimal ClosingBalance);
