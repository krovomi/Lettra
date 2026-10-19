using Lettra.Contracts.Billing;
using Lettra.Contracts.Integrity;

namespace Lettra.Contracts.Exports;

/// <summary>
/// Le manifeste du kit contrôle fiscal (<c>manifeste.json</c> à la racine de l'archive) : ce que contient le kit,
/// les verdicts des contrôles et l'empreinte SHA-256 de chaque autre fichier. L'empreinte du manifeste lui-même est
/// celle du kit, affichée à l'écran et à noter dans le procès-verbal de remise.
/// </summary>
public sealed record AuditKitManifestResponse(
    string Format,
    Guid DossierId,
    string CompanyName,
    string Siren,
    DateOnly From,
    DateOnly To,
    DateTimeOffset GeneratedAt,
    AuditKitFecSummary Fec,
    AuditKitIntegritySummary Integrity,
    IReadOnlyList<AuditKitPieceResponse> Pieces,
    int PaymentCount,
    int TransmissionCount,
    IReadOnlyList<AuditKitFileResponse> Files);

/// <summary>Le FEC du kit et le verdict du validateur intégré.</summary>
public sealed record AuditKitFecSummary(string FileName, bool IsValid, int EntryCount, int ErrorCount, int WarningCount, decimal TotalDebit, decimal TotalCredit);

/// <summary>Le verdict du contrôle de la chaîne anti-fraude fait à la préparation du kit.</summary>
public sealed record AuditKitIntegritySummary(
    bool Intact,
    long EntriesChecked,
    int ClosingsChecked,
    int DocumentsChecked,
    int FindingCount,
    IReadOnlyList<int> ClosingNumbers,
    IReadOnlyList<string> KeyIds);

/// <summary>
/// Une pièce de la période (facture, acompte ou avoir validé) : son XML CII est dans <paramref name="XmlFile"/> ;
/// l'écran y ajoute le PDF Factur-X <paramref name="PdfFile"/> qui embarque ce même XML.
/// </summary>
public sealed record AuditKitPieceResponse(
    Guid InvoiceId,
    string Number,
    InvoiceKindDto Kind,
    DateOnly IssueDate,
    string CustomerName,
    decimal TotalInclTax,
    string XmlFile,
    string PdfFile);

/// <summary>Un fichier du kit et son empreinte SHA-256 (hexadécimal, minuscules).</summary>
public sealed record AuditKitFileResponse(string Path, string Sha256, long Size);

/// <summary>
/// L'attestation d'intégrité (<c>integrite/attestation.json</c>) : le contrôle de toute la chaîne fait à la
/// préparation du kit, le registre des clés publiques et les clôtures signées qui recouvrent la période.
/// </summary>
public sealed record AuditKitAttestationResponse(
    Guid DossierId,
    DateOnly From,
    DateOnly To,
    IntegrityReportResponse Report,
    IReadOnlyList<IntegrityKeySummary> Keys,
    IReadOnlyList<ClosingSummary> Closings);
