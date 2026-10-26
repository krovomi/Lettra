using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Vigilance;

/// <summary>Niveau de risque de blanchiment du client.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RiskLevelDto>))]
public enum RiskLevelDto
{
    Low = 1,
    Standard = 2,
    High = 3,
}

/// <summary>Un facteur qui fait monter le risque.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RiskFactorDto>))]
public enum RiskFactorDto
{
    PoliticallyExposed = 1,
    HighRiskCountry = 2,
    CashIntensive = 4,
    ComplexStructure = 8,
    RemoteRelationship = 16,
    UnusualTransactions = 32,
}

/// <summary>Une pièce d'identification reçue.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IdentityEvidenceDto>))]
public enum IdentityEvidenceDto
{
    CompanyRegistration = 1,
    ManagerIdentity = 2,
    BeneficialOwnersRegister = 4,
    Statutes = 8,
}

/// <summary>Pourquoi une personne est bénéficiaire effectif.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OwnershipKindDto>))]
public enum OwnershipKindDto
{
    Capital = 1,
    VotingRights = 2,
    Control = 3,
    LegalRepresentative = 4,
}

/// <summary>Où en est la fiche : absente, incomplète, à revoir, à jour, relation terminée.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DueDiligenceStatusDto>))]
public enum DueDiligenceStatusDto
{
    Missing = 0,
    Incomplete = 1,
    ReviewDue = 2,
    UpToDate = 3,
    Ended = 4,
}

/// <summary>Un bénéficiaire effectif : sa part au-delà de 25 % (capital, droits de vote), vide pour un contrôle ou un représentant légal.</summary>
public sealed record BeneficialOwnerDto
{
    [Required(ErrorMessage = ErrorCodes.Vigilance.OwnerNameInvalid)]
    [StringLength(120, ErrorMessage = ErrorCodes.Vigilance.OwnerNameInvalid)]
    public string Name { get; set; } = string.Empty;

    public decimal? SharePercent { get; set; }

    public OwnershipKindDto Kind { get; set; } = OwnershipKindDto.Capital;
}

/// <summary>La fiche anti-blanchiment du client, telle qu'enregistrée.</summary>
public sealed record DueDiligenceResponse(
    IReadOnlyList<IdentityEvidenceDto> Evidence,
    DateOnly? IdentityVerifiedOn,
    IReadOnlyList<BeneficialOwnerDto> Owners,
    RiskLevelDto Level,
    IReadOnlyList<RiskFactorDto> Factors,
    string? Justification,
    DateOnly ReviewedOn,
    Guid ReviewedBy,
    string? ReviewedByName,
    DateOnly NextReviewOn,
    DateOnly? RelationshipEndedOn,
    DateOnly? KeepUntil,
    long Version);

/// <summary>L'écran « Anti-blanchiment » : la fiche (vide tant qu'elle n'est pas saisie) et où elle en est aujourd'hui.</summary>
public sealed record VigilanceOverviewResponse(DueDiligenceResponse? File, DueDiligenceStatusDto Status);

/// <summary>Enregistrer la fiche, ce qui vaut revue (concurrence optimiste par <c>If-Match</c>).</summary>
public sealed record SaveDueDiligenceRequest
{
    public IReadOnlyList<IdentityEvidenceDto> Evidence { get; set; } = [];

    public DateOnly? IdentityVerifiedOn { get; set; }

    public IReadOnlyList<BeneficialOwnerDto> Owners { get; set; } = [];

    public RiskLevelDto Level { get; set; } = RiskLevelDto.Standard;

    public IReadOnlyList<RiskFactorDto> Factors { get; set; } = [];

    [StringLength(1000, ErrorMessage = ErrorCodes.Vigilance.JustificationTooLong)]
    public string? Justification { get; set; }

    public DateOnly ReviewedOn { get; set; }

    public DateOnly? RelationshipEndedOn { get; set; }
}
