using System.ComponentModel.DataAnnotations;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Suppliers;

/// <summary>
/// Création d'un fournisseur. Annotations = retour immédiat à l'écran (codes, traduits par l'UI) ;
/// le domaine reste seul juge (SIREN obligatoire en France, cohérence SIRET et TVA, clé IBAN).
/// Propriétés modifiables : le formulaire Blazor s'y lie directement.
/// </summary>
public sealed record CreateSupplierRequest
{
    [Required(ErrorMessage = ErrorCodes.Supplier.NameRequired)]
    [StringLength(200, ErrorMessage = ErrorCodes.Supplier.NameTooLong)]
    public string Name { get; set; } = string.Empty;

    [StringLength(11, ErrorMessage = ErrorCodes.SharedKernel.SirenInvalid)]
    public string? Siren { get; set; }

    [StringLength(17, ErrorMessage = ErrorCodes.SharedKernel.SiretInvalid)]
    public string? Siret { get; set; }

    [StringLength(17, ErrorMessage = ErrorCodes.SharedKernel.VatNumberInvalid)]
    public string? VatNumber { get; set; }

    [StringLength(254, ErrorMessage = ErrorCodes.SharedKernel.EmailInvalid)]
    public string? Email { get; set; }

    [StringLength(42, ErrorMessage = ErrorCodes.SharedKernel.IbanInvalid)]
    public string? Iban { get; set; }

    [Required(ErrorMessage = ErrorCodes.SharedKernel.AddressLineRequired)]
    [StringLength(200, ErrorMessage = ErrorCodes.Validation.TooLong)]
    public string AddressLine1 { get; set; } = string.Empty;

    [StringLength(200, ErrorMessage = ErrorCodes.Validation.TooLong)]
    public string? AddressLine2 { get; set; }

    [Required(ErrorMessage = ErrorCodes.SharedKernel.PostalCodeInvalid)]
    [StringLength(16, ErrorMessage = ErrorCodes.SharedKernel.PostalCodeInvalid)]
    public string PostalCode { get; set; } = string.Empty;

    [Required(ErrorMessage = ErrorCodes.SharedKernel.CityRequired)]
    [StringLength(200, ErrorMessage = ErrorCodes.Validation.TooLong)]
    public string City { get; set; } = string.Empty;

    [Required(ErrorMessage = ErrorCodes.SharedKernel.CountryCodeInvalid)]
    [StringLength(2, MinimumLength = 2, ErrorMessage = ErrorCodes.SharedKernel.CountryCodeInvalid)]
    public string CountryCode { get; set; } = "FR";
}

/// <summary>Fournisseur tel qu'affiché.</summary>
public sealed record SupplierResponse(
    Guid Id,
    string Name,
    string? Siren,
    string? Siret,
    string? VatNumber,
    string? Email,
    string? Iban,
    string AddressLine1,
    string? AddressLine2,
    string PostalCode,
    string City,
    string CountryCode,
    DateTimeOffset CreatedAt)
{
    /// <summary>Version de concurrence optimiste, renvoyée aussi en <c>ETag</c> ; à renvoyer en <c>If-Match</c> pour modifier.</summary>
    public long Version { get; init; }
}

/// <summary>Ligne de la liste des fournisseurs.</summary>
public sealed record SupplierSummary(Guid Id, string Name, string? Siren, string City);
