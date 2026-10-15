using Lettra.Domain.Integrity;

namespace Lettra.Infrastructure.Persistence.Integrity;

/// <summary>Une entrée du journal telle qu'elle est écrite dans l'archive scellée (une ligne JSON).</summary>
internal sealed record ArchivedEntry(
    long Sequence,
    long OccurredAtUnixMs,
    JournalEntryKind Kind,
    JournaledDocumentKind? DocumentKind,
    Guid? DocumentId,
    string? SeriesCode,
    string? Number,
    DateOnly? IssueDate,
    Guid? CustomerId,
    decimal TotalExclTax,
    decimal TotalVat,
    decimal TotalInclTax,
    string? Currency,
    string ContentHash,
    Guid? RelatedDocumentId,
    string PreviousHash,
    string Hash)
{
    public static ArchivedEntry From(JournalEntry e) => new(
        e.Sequence, e.OccurredAt.ToUnixTimeMilliseconds(), e.Kind, e.DocumentKind, e.DocumentId, e.SeriesCode, e.Number, e.IssueDate,
        e.CustomerId, e.TotalExclTax, e.TotalVat, e.TotalInclTax, e.Currency, e.ContentHash, e.RelatedDocumentId, e.PreviousHash, e.Hash);

    // Le sceau (HMAC à clé secrète) ne sort pas : il ne sert qu'au contrôle interne, l'archive repose sur la signature.
    public JournalEntry ToEntry() => JournalEntry.Restore(
        Sequence, DateTimeOffset.FromUnixTimeMilliseconds(OccurredAtUnixMs), Kind, DocumentKind, DocumentId, SeriesCode, Number, IssueDate,
        CustomerId, (TotalExclTax, TotalVat, TotalInclTax), Currency, ContentHash, RelatedDocumentId, (PreviousHash, Hash, string.Empty));
}

/// <summary>La clôture telle qu'elle est écrite dans l'archive scellée.</summary>
internal sealed record ArchivedClosing(
    int Number,
    ClosingPeriodKind PeriodKind,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    long FirstSequence,
    long LastSequence,
    string LastEntryHash,
    decimal TotalExclTax,
    decimal TotalVat,
    decimal TotalInclTax,
    decimal CumulativeInclTax,
    string PreviousClosingHash,
    long ClosedAtUnixMs,
    string KeyId,
    string PublicKey,
    string Hash,
    string Signature)
{
    public static ArchivedClosing From(PeriodClosing c) => new(
        c.Number, c.PeriodKind, c.PeriodStart, c.PeriodEnd, c.FirstSequence, c.LastSequence, c.LastEntryHash, c.TotalExclTax, c.TotalVat,
        c.TotalInclTax, c.CumulativeInclTax, c.PreviousClosingHash, c.ClosedAt.ToUnixTimeMilliseconds(), c.KeyId, c.PublicKey, c.Hash, c.Signature);

    public PeriodClosing ToClosing() => PeriodClosing.Restore(new PeriodClosing.Snapshot(
        Number, PeriodKind, PeriodStart, PeriodEnd, FirstSequence, LastSequence, LastEntryHash, TotalExclTax, TotalVat, TotalInclTax,
        CumulativeInclTax, PreviousClosingHash, DateTimeOffset.FromUnixTimeMilliseconds(ClosedAtUnixMs), KeyId, PublicKey, Hash, Signature));
}
