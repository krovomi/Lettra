using System.Text.Json.Serialization;

namespace Lettra.Contracts.Tax;

/// <summary>La famille d'un seuil suivi : elle dit l'écran où le traiter et ce qui change au franchissement.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ThresholdFamilyDto>))]
public enum ThresholdFamilyDto
{
    /// <summary>Franchise en base de TVA.</summary>
    VatFranchise = 1,

    /// <summary>Régime simplifié de TVA (jusqu'au 31 décembre 2026).</summary>
    VatSimplifiedRegime = 2,

    /// <summary>Déclaration de TVA trimestrielle plutôt que mensuelle (à partir de 2027).</summary>
    VatQuarterlyReturn = 3,

    /// <summary>Catégorie de l'entreprise (micro, PME, ETI) : calendrier de la facture électronique.</summary>
    CompanySize = 4,

    /// <summary>Attestation de vigilance d'un sous-traitant ou prestataire.</summary>
    SubcontractorVigilance = 5,

    /// <summary>Honoraires à déclarer (DAS 2) d'un bénéficiaire.</summary>
    FeeReturn = 6,
}

/// <summary>
/// Le palier d'un seuil à deux niveaux : le seuil de base compte sur l'année entière et change les règles de l'année
/// suivante ; le seuil majoré compte sur l'année en cours et les change tout de suite. Un seuil simple (<see cref="Simple"/>) n'a qu'un niveau.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ThresholdTierDto>))]
public enum ThresholdTierDto
{
    Simple = 1,
    Base = 2,
    Increased = 3,
}

/// <summary>Ce que le seuil compte : des montants hors taxes ou toutes taxes comprises.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ThresholdBasisDto>))]
public enum ThresholdBasisDto
{
    ExclTax = 1,
    InclTax = 2,
}

/// <summary>Où en est un seuil : loin, à surveiller (80 % de la projection), franchissement prévu, déjà franchi.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ThresholdStatusDto>))]
public enum ThresholdStatusDto
{
    Far = 0,
    Near = 1,
    Projected = 2,
    Crossed = 3,
}

/// <summary>
/// Un seuil suivi pour le dossier : son montant et sa source, le cumul de la période et sa projection à la fin de la
/// période, la date de franchissement constatée ou prévue, et la date à laquelle les règles changent.
/// <paramref name="Subject"/> distingue deux lignes du même code (le fournisseur, le bénéficiaire) ;
/// <paramref name="Soon"/> : franchi, ou franchissement prévu dans les trois mois (cartes de l'accueil et de « En bref »).
/// </summary>
public sealed record ThresholdWatchResponse(
    string Code,
    ThresholdFamilyDto Family,
    ThresholdTierDto Tier,
    string Subject,
    Guid? PartyId,
    string? PartyName,
    decimal Threshold,
    ThresholdBasisDto Basis,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal ToDate,
    decimal Projected,
    decimal Excess,
    ThresholdStatusDto Status,
    DateOnly? CrossedOn,
    DateOnly? ExpectedOn,
    DateOnly? EffectiveOn,
    bool Soon,
    string Source);

/// <summary>
/// Les seuils qui approchent : chaque seuil qui s'applique au dossier, le plus urgent d'abord. La projection suit le
/// rythme des <paramref name="RateDays"/> derniers jours ; <paramref name="SoonMonths"/> borne les seuils « proches ».
/// <paramref name="ProfileMissing"/> : sans fiche entreprise, les seuils de TVA et de taille ne se lisent pas.
/// </summary>
public sealed record ThresholdsResponse(
    DateOnly Today,
    int RateDays,
    int SoonMonths,
    bool ProfileMissing,
    IReadOnlyList<ThresholdWatchResponse> Rows);
