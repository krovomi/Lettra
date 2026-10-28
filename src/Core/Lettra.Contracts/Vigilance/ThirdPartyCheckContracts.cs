using System.Text.Json.Serialization;

namespace Lettra.Contracts.Vigilance;

/// <summary>Le tiers vérifié : un client ou un fournisseur.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ThirdPartyKindDto>))]
public enum ThirdPartyKindDto
{
    Customer = 1,
    Supplier = 2,
}

/// <summary>Criblage sur le registre des gels des avoirs : rien, ou une correspondance possible à examiner.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FreezeScreeningDto>))]
public enum FreezeScreeningDto
{
    Clear = 1,
    PossibleMatch = 2,
}

/// <summary>Une annonce du BODACC qui change la relation : procédure collective ou radiation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LegalNoticeKindDto>))]
public enum LegalNoticeKindDto
{
    CollectiveProceeding = 1,
    StruckOff = 2,
}

/// <summary>Réponse de l'URSSAF sur le code de sécurité d'une attestation de vigilance.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<UrssafAnswerDto>))]
public enum UrssafAnswerDto
{
    Recognized = 1,
    NotRecognized = 2,
}

/// <summary>Où en est la vérification d'un tiers : jamais vérifié, à examiner, à refaire (plus de six mois), rien à signaler.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ThirdPartyCheckStatusDto>))]
public enum ThirdPartyCheckStatusDto
{
    NeverChecked = 0,
    ToReview = 1,
    Outdated = 2,
    Clear = 3,
}

/// <summary>
/// Un tiers et sa dernière vérification auprès des registres. Les champs de la vérification sont vides tant qu'il n'a
/// jamais été vérifié ; <paramref name="OwnersFound"/> l'est aussi pour un tiers sans SIREN.
/// </summary>
public sealed record ThirdPartyCheckRow(
    ThirdPartyKindDto Kind,
    Guid PartyId,
    string Name,
    string? Siren,
    ThirdPartyCheckStatusDto Status,
    DateTimeOffset? CheckedAt,
    int? OwnersFound,
    FreezeScreeningDto? Freeze,
    string? FreezeMatch,
    LegalNoticeKindDto? Notice,
    DateOnly? NoticeOn,
    string? NoticeLabel,
    UrssafAnswerDto? Urssaf);

/// <summary>Les clients et fournisseurs du dossier et leur dernière vérification, ceux à examiner d'abord.</summary>
public sealed record ThirdPartyChecksResponse(IReadOnlyList<ThirdPartyCheckRow> Rows);

/// <summary>Un bénéficiaire effectif lu au registre, et s'il ressemble à une personne gelée.</summary>
public sealed record RegisteredOwnerDto(string Name, OwnershipKindDto Kind, decimal? SharePercent, bool FreezeMatch);

/// <summary>
/// Les bénéficiaires effectifs de l'entreprise du dossier lus au registre, pour remplir la fiche anti-blanchiment.
/// <paramref name="Siren"/> vide : la fiche de l'entreprise n'a pas de SIREN, rien n'a été demandé.
/// <paramref name="CompanyFreezeMatch"/> : le nom inscrit au registre des gels qui ressemble à l'entreprise elle-même.
/// </summary>
public sealed record RegisteredOwnersResponse(string? Siren, IReadOnlyList<RegisteredOwnerDto> Owners, string? CompanyFreezeMatch);

/// <summary>Réponse de l'URSSAF sur le code d'une attestation, et les attestations après la vérification.</summary>
public sealed record AttestationVerificationResponse(UrssafAnswerDto Answer, SupplierAttestationsResponse Attestations);
