using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Integrity;

/// <summary>Ce qui entre dans le journal. Les valeurs sont stockées : ajout seulement, jamais de renumérotation.</summary>
public enum JournalEntryKind
{
    QuoteValidated = 1,
    InvoiceValidated = 2,
    InvoiceIssued = 3,
    CreditNoteValidated = 4,
    CreditNoteIssued = 5,
    DocumentCancelled = 6,

    /// <summary>Une clôture périodique signée : elle entre elle-même dans la chaîne.</summary>
    PeriodClosed = 100,
}

/// <summary>Nature de la pièce journalisée. Ajout seulement.</summary>
public enum JournaledDocumentKind
{
    Quote = 1,
    Invoice = 2,
    DepositInvoice = 3,
    CreditNote = 4,
}

/// <summary>
/// Un événement du cycle de vie d'une pièce, publié par le module Devis et Facturation dans la transaction qui le
/// produit. Les montants sont ceux de la pièce, positifs, déjà arrondis par <c>Lettra.Calculations</c> ; la chaîne
/// soustrait elle-même les avoirs dans les totaux des clôtures. <c>ContentHash</c> est l'empreinte du contenu figé
/// (<see cref="DocumentFingerprint"/>) ; <c>RelatedDocumentId</c> la facture d'origine d'un avoir, ou le devis
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

/// <summary>
/// Une entrée du journal append-only d'un dossier (phase 3, chaîne anti-fraude). Chaque entrée porte l'empreinte
/// SHA-256 de son contenu et de l'empreinte précédente : modifier, insérer ou supprimer une ligne casse la chaîne.
/// Le sceau (HMAC par une clé dérivée de la clé du cabinet, qui n'est pas en base) empêche de recalculer la chaîne
/// après coup sans la clé.
/// </summary>
public sealed class JournalEntry
{
    public const int MaxSeriesLength = 16;
    public const int MaxNumberLength = 40;
    public const int HashLength = 64;

    private JournalEntry()
    {
    }

    public long Sequence { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public JournalEntryKind Kind { get; private set; }

    public JournaledDocumentKind? DocumentKind { get; private set; }

    public Guid? DocumentId { get; private set; }

    public string? SeriesCode { get; private set; }

    public string? Number { get; private set; }

    public DateOnly? IssueDate { get; private set; }

    public Guid? CustomerId { get; private set; }

    public decimal TotalExclTax { get; private set; }

    public decimal TotalVat { get; private set; }

    public decimal TotalInclTax { get; private set; }

    public string? Currency { get; private set; }

    /// <summary>Empreinte du contenu de la pièce, ou de la clôture pour <see cref="JournalEntryKind.PeriodClosed"/>.</summary>
    public string ContentHash { get; private set; } = string.Empty;

    public Guid? RelatedDocumentId { get; private set; }

    public string PreviousHash { get; private set; } = string.Empty;

    public string Hash { get; private set; } = string.Empty;

    public string Seal { get; private set; } = string.Empty;

    /// <summary>Empreinte de départ, propre au dossier : une chaîne ne peut pas être recollée sur un autre dossier.</summary>
    public static string GenesisHash(DossierId dossier) => IntegrityHash.Of("lettra-journal-v1", IntegrityHash.Field(dossier.Value));

    public static Result<JournalEntry> ForDocument(DocumentLifecycleEvent @event, long sequence, string previousHash, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (@event.Kind == JournalEntryKind.PeriodClosed
            || !Enum.IsDefined(@event.Kind)
            || !Enum.IsDefined(@event.DocumentKind)
            || @event.DocumentId == Guid.Empty
            || string.IsNullOrWhiteSpace(@event.SeriesCode) || @event.SeriesCode.Length > MaxSeriesLength
            || string.IsNullOrWhiteSpace(@event.Number) || @event.Number.Length > MaxNumberLength
            || @event.Currency is not { Length: 3 }
            || @event.ContentHash is not { Length: HashLength })
        {
            return IntegrityErrors.EventInvalid;
        }

        return Chain(new JournalEntry
        {
            Kind = @event.Kind,
            DocumentKind = @event.DocumentKind,
            DocumentId = @event.DocumentId,
            SeriesCode = @event.SeriesCode,
            Number = @event.Number,
            IssueDate = @event.IssueDate,
            CustomerId = @event.CustomerId,
            TotalExclTax = @event.TotalExclTax,
            TotalVat = @event.TotalVat,
            TotalInclTax = @event.TotalInclTax,
            Currency = @event.Currency,
            ContentHash = @event.ContentHash,
            RelatedDocumentId = @event.RelatedDocumentId,
        }, sequence, previousHash, at);
    }

    /// <summary>L'entrée qui inscrit une clôture dans la chaîne : son numéro et son empreinte, et les totaux de la période.</summary>
    public static JournalEntry ForClosing(PeriodClosing closing, long sequence, string previousHash, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(closing);
        return Chain(new JournalEntry
        {
            Kind = JournalEntryKind.PeriodClosed,
            Number = PeriodClosing.FormatNumber(closing.Number),
            IssueDate = closing.PeriodEnd,
            TotalExclTax = closing.TotalExclTax,
            TotalVat = closing.TotalVat,
            TotalInclTax = closing.TotalInclTax,
            ContentHash = closing.Hash,
        }, sequence, previousHash, at).Value;
    }

    /// <summary>Reconstitue une entrée telle qu'elle a été lue (archive scellée) : rien n'est recalculé.</summary>
    public static JournalEntry Restore(
        long sequence,
        DateTimeOffset occurredAt,
        JournalEntryKind kind,
        JournaledDocumentKind? documentKind,
        Guid? documentId,
        string? seriesCode,
        string? number,
        DateOnly? issueDate,
        Guid? customerId,
        (decimal ExclTax, decimal Vat, decimal InclTax) totals,
        string? currency,
        string contentHash,
        Guid? relatedDocumentId,
        (string Previous, string Hash, string Seal) chain) => new()
        {
            Sequence = sequence,
            OccurredAt = occurredAt,
            Kind = kind,
            DocumentKind = documentKind,
            DocumentId = documentId,
            SeriesCode = seriesCode,
            Number = number,
            IssueDate = issueDate,
            CustomerId = customerId,
            TotalExclTax = totals.ExclTax,
            TotalVat = totals.Vat,
            TotalInclTax = totals.InclTax,
            Currency = currency,
            ContentHash = contentHash,
            RelatedDocumentId = relatedDocumentId,
            PreviousHash = chain.Previous,
            Hash = chain.Hash,
            Seal = chain.Seal,
        };

    /// <summary>Recalcule l'empreinte à partir des champs tels qu'ils sont (en base, dans une archive).</summary>
    public string ComputeHash() => IntegrityHash.Of(
        "entry-v1",
        IntegrityHash.Field(Sequence),
        IntegrityHash.Field(OccurredAt),
        IntegrityHash.Field((int)Kind),
        DocumentKind is { } kind ? IntegrityHash.Field((int)kind) : null,
        IntegrityHash.Field(DocumentId),
        SeriesCode,
        Number,
        IntegrityHash.Field(IssueDate),
        IntegrityHash.Field(CustomerId),
        IntegrityHash.Field(TotalExclTax),
        IntegrityHash.Field(TotalVat),
        IntegrityHash.Field(TotalInclTax),
        Currency,
        ContentHash,
        IntegrityHash.Field(RelatedDocumentId),
        PreviousHash);

    /// <summary>Contribution signée de l'entrée au chiffre d'affaires : facture +, avoir −, le reste 0.</summary>
    public (decimal ExclTax, decimal Vat, decimal InclTax) RevenueContribution() => Kind switch
    {
        JournalEntryKind.InvoiceValidated => (TotalExclTax, TotalVat, TotalInclTax),
        JournalEntryKind.CreditNoteValidated => (-TotalExclTax, -TotalVat, -TotalInclTax),
        _ => (0m, 0m, 0m),
    };

    /// <summary>Vrai si l'entrée consomme un numéro de série (validation d'une pièce).</summary>
    public bool AssignsNumber => Kind is JournalEntryKind.QuoteValidated or JournalEntryKind.InvoiceValidated or JournalEntryKind.CreditNoteValidated;

    public void ApplySeal(string seal)
    {
        ArgumentException.ThrowIfNullOrEmpty(seal);
        Seal = seal;
    }

    private static Result<JournalEntry> Chain(JournalEntry entry, long sequence, string previousHash, DateTimeOffset at)
    {
        entry.Sequence = sequence;
        entry.OccurredAt = IntegrityHash.Truncate(at);
        entry.PreviousHash = previousHash;
        entry.Hash = entry.ComputeHash();
        return entry;
    }
}
