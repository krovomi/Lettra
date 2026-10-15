namespace Lettra.Domain.Integrity;

/// <summary>Événement inscrit au journal inaltérable (chaîne anti-fraude). 100 est réservé à la clôture de période.</summary>
public enum JournalEntryKind
{
    QuoteValidated = 1,
    InvoiceValidated = 2,
    InvoiceIssued = 3,
    CreditNoteValidated = 4,
    CreditNoteIssued = 5,
    DocumentCancelled = 6,
}

/// <summary>Nature de la pièce journalisée.</summary>
public enum JournaledDocumentKind
{
    Quote = 1,
    Invoice = 2,
    DepositInvoice = 3,
    CreditNote = 4,
}

/// <summary>
/// Une étape du cycle de vie d'une pièce, telle qu'elle entre dans le journal inaltérable. Montants positifs, tels que
/// sur la pièce ; <paramref name="ContentHash"/> est l'empreinte SHA-256 (64 caractères hexadécimaux) de la forme
/// canonique de la pièce ; <paramref name="RelatedDocumentId"/> désigne la facture d'origine d'un avoir ou le devis
/// d'origine d'une facture.
/// </summary>
public sealed record DocumentLifecycleEvent(
    JournalEntryKind Kind,
    JournaledDocumentKind DocumentKind,
    Guid DocumentId,
    string SeriesCode,
    string Number,
    DateOnly IssueDate,
    Guid CustomerId,
    decimal TotalExclTax,
    decimal TotalVat,
    decimal TotalInclTax,
    string Currency,
    string ContentHash,
    Guid? RelatedDocumentId = null);
