namespace Lettra.Contracts.Integrity;

/// <summary>Résultat d'un contrôle d'intégrité de la chaîne anti-fraude. Aucune donnée métier : des numéros d'entrée.</summary>
public sealed record IntegrityReportResponse(
    bool Intact,
    DateTimeOffset CheckedAt,
    long EntriesChecked,
    int ClosingsChecked,
    int DocumentsChecked,
    IReadOnlyList<IntegrityFindingResponse> Findings,
    bool FindingsTruncated);

/// <summary>Un constat : <c>Kind</c> est traduit par l'interface (<c>Integrity.Finding.&lt;Kind&gt;</c>).</summary>
public sealed record IntegrityFindingResponse(string Kind, long? Sequence, int? ClosingNumber, string? SeriesCode);

/// <summary>Une clôture signée du dossier.</summary>
public sealed record ClosingSummary(
    int Number,
    string PeriodKind,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    long FirstSequence,
    long LastSequence,
    decimal TotalExclTax,
    decimal TotalVat,
    decimal TotalInclTax,
    decimal CumulativeInclTax,
    DateTimeOffset ClosedAt,
    string KeyId,
    string Hash);

/// <summary>
/// Une version du registre des clés publiques du dossier. Une clôture ou une archive se vérifie avec la clé publique
/// de son <c>KeyId</c> ; une version retirée reste au registre pour vérifier l'historique.
/// </summary>
public sealed record IntegrityKeySummary(
    int Version,
    string KeyId,
    string PublicKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RetiredAt,
    bool Current);
