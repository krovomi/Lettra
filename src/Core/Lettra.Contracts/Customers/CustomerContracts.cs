using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Customers;

/// <summary>Nature du client.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CustomerKindDto>))]
public enum CustomerKindDto
{
    Company = 1,
    Individual = 2,
}

/// <summary>
/// Création d'un client. Les annotations donnent un retour immédiat à l'écran (EditForm) et
/// sont réévaluées par l'API ; le domaine reste le seul juge des règles métier (SIREN, cohérence SIRET).
/// Les messages d'annotation sont des <b>codes</b>, traduits par l'UI.
/// Propriétés modifiables : le formulaire Blazor s'y lie directement (<c>@bind-Value</c>).
/// </summary>
public sealed record CreateCustomerRequest
{
    public CustomerKindDto Kind { get; set; } = CustomerKindDto.Company;

    [Required(ErrorMessage = ErrorCodes.Customer.NameRequired)]
    [StringLength(200, ErrorMessage = ErrorCodes.Customer.NameTooLong)]
    public string Name { get; set; } = string.Empty;

    [StringLength(11, ErrorMessage = ErrorCodes.SharedKernel.SirenInvalid)]
    public string? Siren { get; set; }

    [StringLength(17, ErrorMessage = ErrorCodes.SharedKernel.SiretInvalid)]
    public string? Siret { get; set; }

    [StringLength(17, ErrorMessage = ErrorCodes.SharedKernel.VatNumberInvalid)]
    public string? VatNumber { get; set; }

    [StringLength(254, ErrorMessage = ErrorCodes.SharedKernel.EmailInvalid)]
    public string? Email { get; set; }

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

/// <summary>Client tel qu'affiché.</summary>
public sealed record CustomerResponse(
    Guid Id,
    CustomerKindDto Kind,
    string Name,
    string? Siren,
    string? Siret,
    string? VatNumber,
    string? Email,
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

/// <summary>Ligne de la liste des clients.</summary>
public sealed record CustomerSummary(Guid Id, CustomerKindDto Kind, string Name, string? Siren, string City);
