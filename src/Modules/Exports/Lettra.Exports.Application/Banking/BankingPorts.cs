using Lettra.Calculations;
using Lettra.Domain.Banking;
using Lettra.Domain.Billing;
using Lettra.Domain.SharedKernel;

namespace Lettra.Exports.Application.Banking;

/// <summary>Une écriture du journal de banque (BQ) : un encaissement ou sa contre-passation.</summary>
public sealed record BankJournalEntry(
    PaymentId PaymentId,
    bool IsReversal,
    string PieceNumber,
    DateOnly Date,
    CustomerId CustomerId,
    string CustomerName,
    IReadOnlyList<AccountingEntryLine> Lines);

/// <summary>Une pièce de vente validée vue par les encaissements : montants, reste dû, et avoir éventuel.</summary>
public sealed record ReceivableDocument(
    InvoiceId InvoiceId,
    InvoiceKind Kind,
    string Number,
    CustomerId CustomerId,
    string CustomerName,
    DateOnly IssueDate,
    DateOnly? DueDate,
    decimal TotalInclTax,
    decimal CreditedInclTax,
    decimal PaidInclTax,
    InvoiceId? CreditedInvoiceId)
{
    public decimal Outstanding => TotalInclTax - CreditedInclTax - PaidInclTax;
}

/// <summary>Ce qu'on retient du rapprochement d'un dossier pour la vue cabinet.</summary>
public sealed record BankReconciliationStatus(
    DateTimeOffset? LastImportAt,
    DateOnly? LastStatementDate,
    int PendingLines,
    DateOnly? OldestPendingDate,
    DateTimeOffset? LastMatchedAt);

/// <summary>Filtre de la liste des encaissements.</summary>
public sealed record PaymentListFilter(DateOnly? From, DateOnly? To, CustomerId? Customer, string? Search, int Page, int PageSize);

/// <summary>Dépôt des encaissements (base du dossier).</summary>
public interface IPaymentRepository
{
    Task AddAsync(Payment payment, CancellationToken cancellationToken);

    Task<Payment?> FindAsync(PaymentId id, CancellationToken cancellationToken);

    /// <summary>Encaissements non contre-passés d'un client (lecture seule), pour le lettrage.</summary>
    Task<IReadOnlyList<Payment>> ListActiveForCustomerAsync(CustomerId customer, CancellationToken cancellationToken);

    Task<(IReadOnlyList<Payment> Items, int Total)> ListAsync(PaymentListFilter filter, CancellationToken cancellationToken);

    /// <summary>Numéros des encaissements donnés, pour l'écran de rapprochement.</summary>
    Task<IReadOnlyDictionary<Guid, string>> NumbersAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

/// <summary>Le journal de banque (BQ), écrit dans la transaction de l'encaissement.</summary>
public interface IBankJournal
{
    Task AddAsync(BankJournalEntry entry, CancellationToken cancellationToken);

    Task<IReadOnlyList<BankJournalEntry>> ForPaymentAsync(PaymentId payment, CancellationToken cancellationToken);

    /// <summary>Écritures datées de <paramref name="from"/> à <paramref name="to"/>, dans l'ordre d'enregistrement.</summary>
    Task<IReadOnlyList<BankJournalEntry>> ReadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

/// <summary>Relevés importés et leurs opérations.</summary>
public interface IBankStatementRepository
{
    Task<IReadOnlySet<string>> ExistingFingerprintsAsync(IReadOnlyCollection<string> fingerprints, CancellationToken cancellationToken);

    Task AddImportAsync(BankStatementImport import, IReadOnlyList<BankStatementLine> lines, CancellationToken cancellationToken);

    Task<BankStatementLine?> FindLineAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Les opérations à rapprocher, puis les plus récentes des autres, dans la limite donnée.</summary>
    Task<IReadOnlyList<BankStatementLine>> ListLinesAsync(int max, CancellationToken cancellationToken);

    Task<(int Pending, int Matched, int Ignored)> CountAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<BankStatementImport>> ListImportsAsync(int max, CancellationToken cancellationToken);

    Task<BankReconciliationStatus> StatusAsync(CancellationToken cancellationToken);
}

/// <summary>Lettrages des comptes clients.</summary>
public interface ILetteringRepository
{
    Task AddAsync(Lettering lettering, CancellationToken cancellationToken);

    /// <summary>Tous les lettrages d'un client, défaits compris (suivis : un délettrage les modifie).</summary>
    Task<IReadOnlyList<Lettering>> ListForCustomerAsync(CustomerId customer, CancellationToken cancellationToken);

    /// <summary>Les lettrages actifs qui contiennent l'une des écritures données (lecture seule).</summary>
    Task<IReadOnlyList<Lettering>> ListActiveContainingAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken);

    /// <summary>Tous les lettrages actifs du dossier (lecture seule), pour le FEC et la consultation.</summary>
    Task<IReadOnlyList<Lettering>> ListActiveAsync(CancellationToken cancellationToken);
}

/// <summary>Réglages du rapprochement (une ligne par dossier, créée au premier réglage).</summary>
public interface IBankingSettingsRepository
{
    Task<BankingSettings?> FindAsync(CancellationToken cancellationToken);

    Task AddAsync(BankingSettings settings, CancellationToken cancellationToken);
}

/// <summary>IBAN déjà vus pour les clients (empreintes).</summary>
public interface IKnownIbanStore
{
    Task<IReadOnlyList<KnownIban>> ListAsync(CancellationToken cancellationToken);

    Task RememberAsync(CustomerId customer, string ibanHash, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>Lecture des pièces de vente validées et de leur TVA en attente (lecture seule).</summary>
public interface IReceivableDocumentReader
{
    /// <summary>Factures, acomptes et avoirs validés, d'un client ou de tout le dossier.</summary>
    Task<IReadOnlyList<ReceivableDocument>> ListAsync(CustomerId? customer, CancellationToken cancellationToken);

    /// <summary>
    /// TVA encore en attente (44574) par facture et par taux : celle de son écriture de vente, moins celle de ses avoirs,
    /// moins les parts déjà rendues exigibles par ses encaissements non contre-passés.
    /// </summary>
    Task<IReadOnlyDictionary<InvoiceId, IReadOnlyDictionary<decimal, decimal>>> PendingVatAsync(IReadOnlyCollection<InvoiceId> invoices, CancellationToken cancellationToken);
}
