using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;

namespace Lettra.Contracts.Billing;

/// <summary>
/// Régime de déclaration de la TVA de l'entreprise (CA3 ou CA12) ; il fixe aussi le rythme de l'e-reporting.
/// Sans objet en franchise en base.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<VatDeclarationRegimeDto>))]
public enum VatDeclarationRegimeDto
{
    /// <summary>Réel normal : CA3 mensuelle (ou trimestrielle sous 4 000 € de TVA par an).</summary>
    RealNormal = 1,

    /// <summary>Réel simplifié : CA12 annuelle et deux acomptes.</summary>
    RealSimplified = 2,

    /// <summary>Mini-réel : TVA au réel normal, bénéfices au réel simplifié.</summary>
    MiniReal = 3,
}

/// <summary>
/// Catégorie de l'entreprise (grande entreprise, ETI, PME, micro-entreprise) : elle fixe la date à laquelle l'entreprise
/// émet ses factures électroniques, fait son e-reporting et porte les nouvelles mentions (1er septembre 2026 ou 2027).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CompanySizeDto>))]
public enum CompanySizeDto
{
    /// <summary>Non renseignée : la première échéance s'applique.</summary>
    Unknown = 0,

    /// <summary>Grande entreprise.</summary>
    Large = 1,

    /// <summary>Entreprise de taille intermédiaire (ETI).</summary>
    Intermediate = 2,

    /// <summary>Petite ou moyenne entreprise (PME).</summary>
    SmallOrMedium = 3,

    /// <summary>Micro-entreprise.</summary>
    Micro = 4,
}

/// <summary>
/// Fiche de l'entreprise du dossier : l'émetteur des devis et factures. Ses mentions sont copiées sur chaque
/// pièce à la validation ; la modifier ne change jamais une pièce validée.
/// </summary>
public sealed record SaveCompanyProfileRequest
{
    [Required(ErrorMessage = ErrorCodes.CompanyProfile.NameRequired)]
    [StringLength(200, ErrorMessage = ErrorCodes.CompanyProfile.NameTooLong)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Forme juridique (SAS, SARL, EI…).</summary>
    [StringLength(50, ErrorMessage = ErrorCodes.Validation.TooLong)]
    public string? LegalForm { get; set; }

    [Range(0d, 999_999_999_999d, ErrorMessage = ErrorCodes.Validation.Invalid)]
    public decimal? ShareCapital { get; set; }

    [StringLength(11, ErrorMessage = ErrorCodes.SharedKernel.SirenInvalid)]
    public string? Siren { get; set; }

    [StringLength(17, ErrorMessage = ErrorCodes.SharedKernel.SiretInvalid)]
    public string? Siret { get; set; }

    /// <summary>Greffe d'immatriculation au RCS (« RCS Lyon ») ou au RM.</summary>
    [StringLength(100, ErrorMessage = ErrorCodes.Validation.TooLong)]
    public string? Rcs { get; set; }

    [StringLength(17, ErrorMessage = ErrorCodes.SharedKernel.VatNumberInvalid)]
    public string? VatNumber { get; set; }

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

    [StringLength(2, MinimumLength = 2, ErrorMessage = ErrorCodes.SharedKernel.CountryCodeInvalid)]
    public string CountryCode { get; set; } = "FR";

    /// <summary>Franchise en base de TVA (art. 293 B du CGI) : régime par défaut des nouvelles pièces.</summary>
    public bool VatFranchise { get; set; }

    /// <summary>Option pour le paiement de la TVA d'après les débits (mention 2026).</summary>
    public bool VatOnDebits { get; set; }

    /// <summary>Régime de déclaration de la TVA ; ignoré en franchise en base.</summary>
    public VatDeclarationRegimeDto? VatDeclarationRegime { get; set; }

    /// <summary>Délai de paiement par défaut, en jours après l'émission (60 au plus, art. L441-10 C.com).</summary>
    [Range(0, 60, ErrorMessage = ErrorCodes.CompanyProfile.PaymentDaysInvalid)]
    public int PaymentDays { get; set; } = 30;

    [StringLength(500, ErrorMessage = ErrorCodes.Billing.PaymentTermsTooLong)]
    public string? PaymentTerms { get; set; }

    /// <summary>Taux annuel des pénalités de retard, en pourcentage.</summary>
    [Range(0d, 100d, ErrorMessage = ErrorCodes.Validation.Invalid)]
    public decimal? LatePenaltyRate { get; set; }

    /// <summary>Conditions d'escompte (« Pas d'escompte pour paiement anticipé » si aucun).</summary>
    [StringLength(200, ErrorMessage = ErrorCodes.Validation.TooLong)]
    public string? EarlyPaymentDiscount { get; set; }

    [StringLength(34, ErrorMessage = ErrorCodes.SharedKernel.IbanInvalid)]
    public string? Iban { get; set; }

    [StringLength(11, ErrorMessage = ErrorCodes.Validation.Invalid)]
    public string? Bic { get; set; }

    /// <summary>
    /// Nom exact du titulaire du compte quand il diffère de la raison sociale (70 caractères au plus, limite d'un
    /// virement SEPA) : la banque du client le compare à l'IBAN avant de payer.
    /// </summary>
    [StringLength(70, ErrorMessage = ErrorCodes.Validation.TooLong)]
    public string? AccountHolderName { get; set; }

    /// <summary>Code NAF (APE) de l'activité principale (« 47.24Z ») : il sert à se comparer aux dossiers du même secteur.</summary>
    [StringLength(8, ErrorMessage = ErrorCodes.Validation.Invalid)]
    public string? ActivityCode { get; set; }

    /// <summary>Catégorie de l'entreprise : elle fixe ses dates de la réforme de la facturation électronique.</summary>
    public CompanySizeDto Size { get; set; }

    /// <summary>La fiche enregistrée, prête à être modifiée puis renvoyée.</summary>
    public static SaveCompanyProfileRequest From(CompanyProfileResponse profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new SaveCompanyProfileRequest
        {
            Name = profile.Name,
            LegalForm = profile.LegalForm,
            ShareCapital = profile.ShareCapital,
            Siren = profile.Siren,
            Siret = profile.Siret,
            Rcs = profile.Rcs,
            VatNumber = profile.VatNumber,
            AddressLine1 = profile.AddressLine1,
            AddressLine2 = profile.AddressLine2,
            PostalCode = profile.PostalCode,
            City = profile.City,
            CountryCode = profile.CountryCode,
            VatFranchise = profile.VatFranchise,
            VatOnDebits = profile.VatOnDebits,
            VatDeclarationRegime = profile.VatDeclarationRegime,
            PaymentDays = profile.PaymentDays,
            PaymentTerms = profile.PaymentTerms,
            LatePenaltyRate = profile.LatePenaltyRate,
            EarlyPaymentDiscount = profile.EarlyPaymentDiscount,
            Iban = profile.Iban,
            Bic = profile.Bic,
            AccountHolderName = profile.AccountHolderName,
            ActivityCode = profile.ActivityCode,
            Size = profile.Size,
        };
    }
}

/// <summary>Fiche entreprise telle qu'affichée ; <c>null</c> côté API tant qu'elle n'a pas été remplie.</summary>
public sealed record CompanyProfileResponse(
    long Version,
    string Name,
    string? LegalForm,
    decimal? ShareCapital,
    string? Siren,
    string? Siret,
    string? Rcs,
    string? VatNumber,
    string AddressLine1,
    string? AddressLine2,
    string PostalCode,
    string City,
    string CountryCode,
    bool VatFranchise,
    bool VatOnDebits,
    int PaymentDays,
    string? PaymentTerms,
    decimal? LatePenaltyRate,
    string? EarlyPaymentDiscount,
    string? Iban,
    string? Bic,
    DateTimeOffset UpdatedAt,
    VatDeclarationRegimeDto? VatDeclarationRegime = null,
    string? AccountHolderName = null,
    PayeeMatchDto? HolderNameMatch = null,
    string? ActivityCode = null,
    CompanySizeDto Size = CompanySizeDto.Unknown,
    DateOnly? EInvoicingIssuingFrom = null,
    DateOnly? EInvoicingReceivingFrom = null);
