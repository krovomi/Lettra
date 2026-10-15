using System.Text.Json.Serialization;

namespace Lettra.Contracts.Exports;

/// <summary>Gravité d'un constat du validateur FEC.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FecFindingSeverityDto>))]
public enum FecFindingSeverityDto
{
    /// <summary>Le fichier serait refusé par l'administration (format ou équilibre).</summary>
    Error = 1,

    /// <summary>À vérifier, le fichier reste recevable.</summary>
    Warning = 2,
}

/// <summary>
/// Un constat du validateur FEC. <paramref name="Code"/> est un code <c>Fec.*</c> de
/// <see cref="Common.ErrorCodes.Fec"/>, traduit par l'interface ; <paramref name="Line"/> est le numéro de ligne du
/// fichier (1 = en-tête), <paramref name="Column"/> le nom de la colonne, <paramref name="EntryNumber"/> l'écriture.
/// </summary>
public sealed record FecFindingResponse(
    string Code,
    FecFindingSeverityDto Severity,
    int? Line = null,
    string? Column = null,
    string? EntryNumber = null);

/// <summary>
/// Verdict du validateur intégré sur un fichier des écritures comptables (art. A47 A-1 du LPF) : structure des 18
/// colonnes, formats, équilibre de chaque écriture et du fichier, ordre chronologique. Les constats sont tronqués
/// au-delà de <c>FindingsTruncated</c>.
/// </summary>
public sealed record FecValidationResponse(
    bool IsValid,
    string FileName,
    int LineCount,
    int EntryCount,
    decimal TotalDebit,
    decimal TotalCredit,
    IReadOnlyList<FecFindingResponse> Findings,
    bool FindingsTruncated);
