using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Vigilance;

/// <summary>Où en est l'attestation de vigilance d'un fournisseur.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AttestationStatusDto>))]
public enum AttestationStatusDto
{
    NotRequired = 0,
    Missing = 1,
    Expired = 2,
    NotVerified = 3,
    ExpiringSoon = 4,
    Valid = 5,
}

/// <summary>
/// Un fournisseur et son attestation de vigilance. <paramref name="ContractAmountExclTax"/> et <paramref name="Version"/>
/// sont vides tant que le fournisseur n'est pas suivi (rien n'est alors exigé).
/// </summary>
public sealed record SupplierAttestationRow(
    Guid SupplierId,
    string SupplierName,
    decimal? ContractAmountExclTax,
    DateOnly? IssuedOn,
    DateOnly? ValidUntil,
    string? SecurityCode,
    DateOnly? VerifiedOn,
    AttestationStatusDto Status,
    long? Version);

/// <summary>Les fournisseurs du dossier et leurs attestations de vigilance, ceux à traiter d'abord.</summary>
public sealed record SupplierAttestationsResponse(IReadOnlyList<SupplierAttestationRow> Rows);

/// <summary>Enregistrer le contrat et l'attestation d'un fournisseur (concurrence optimiste par <c>If-Match</c>).</summary>
public sealed record SaveAttestationRequest
{
    [Range(0, 100_000_000, ErrorMessage = ErrorCodes.Attestation.AmountInvalid)]
    public decimal ContractAmountExclTax { get; set; }

    public DateOnly? IssuedOn { get; set; }

    [StringLength(30, ErrorMessage = ErrorCodes.Attestation.CodeInvalid)]
    public string? SecurityCode { get; set; }

    public DateOnly? VerifiedOn { get; set; }
}
