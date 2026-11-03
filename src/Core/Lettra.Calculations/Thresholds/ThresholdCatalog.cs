namespace Lettra.Calculations.Thresholds;

/// <summary>Ce qu'un seuil compte : des montants hors taxes ou toutes taxes comprises.</summary>
public enum ThresholdBasis
{
    ExclTax = 1,
    InclTax = 2,
}

/// <summary>
/// Un seuil réglementaire daté : son montant, la période où il s'applique et le texte qui le fixe. Un nouveau montant
/// s'ajoute en nouvelle ligne du catalogue, l'ancien garde ses dates : un exercice passé se relit avec ses propres seuils.
/// </summary>
public sealed record ThresholdParameter(string Code, decimal Amount, ThresholdBasis Basis, DateOnly ValidFrom, DateOnly? ValidUntil, string Source)
{
    public bool AppliesOn(DateOnly date) => date >= ValidFrom && (ValidUntil is null || date <= ValidUntil);
}

/// <summary>Les codes stables des seuils suivis (clés du catalogue, des textes de l'écran et des futures décisions).</summary>
public static class ThresholdCodes
{
    /// <summary>Franchise en base de TVA, livraisons de biens, ventes à consommer sur place et hébergement : chiffre d'affaires de l'année précédente.</summary>
    public const string FranchiseGoodsBase = "tva.franchise.ventes.base";

    /// <summary>Franchise en base de TVA, livraisons de biens : chiffre d'affaires de l'année en cours.</summary>
    public const string FranchiseGoodsIncreased = "tva.franchise.ventes.majore";

    /// <summary>Franchise en base de TVA, autres prestations de services : chiffre d'affaires de l'année précédente.</summary>
    public const string FranchiseServicesBase = "tva.franchise.services.base";

    /// <summary>Franchise en base de TVA, autres prestations de services : chiffre d'affaires de l'année en cours.</summary>
    public const string FranchiseServicesIncreased = "tva.franchise.services.majore";

    /// <summary>Régime simplifié de TVA, livraisons de biens : seuil majoré de l'année en cours.</summary>
    public const string SimplifiedGoodsIncreased = "tva.regime-simplifie.ventes.majore";

    /// <summary>Régime simplifié de TVA, prestations de services : seuil majoré de l'année en cours.</summary>
    public const string SimplifiedServicesIncreased = "tva.regime-simplifie.services.majore";

    /// <summary>Déclaration de TVA trimestrielle : chiffre d'affaires de l'année précédente.</summary>
    public const string QuarterlyBase = "tva.trimestrielle.base";

    /// <summary>Déclaration de TVA trimestrielle : chiffre d'affaires de l'année en cours.</summary>
    public const string QuarterlyIncreased = "tva.trimestrielle.majore";

    /// <summary>Limite de chiffre d'affaires d'une micro-entreprise (catégorie de la facture électronique).</summary>
    public const string SizeMicro = "taille.micro";

    /// <summary>Limite de chiffre d'affaires d'une PME.</summary>
    public const string SizeSmallOrMedium = "taille.pme";

    /// <summary>Limite de chiffre d'affaires d'une entreprise de taille intermédiaire.</summary>
    public const string SizeIntermediate = "taille.eti";

    /// <summary>Montant d'une prestation à partir duquel l'attestation de vigilance du cocontractant est exigée.</summary>
    public const string Vigilance = "vigilance.sous-traitant";

    /// <summary>Sommes versées à un même bénéficiaire au-delà desquelles elles se déclarent (DAS 2).</summary>
    public const string FeeReturn = "das2.beneficiaire";
}

/// <summary>
/// Le catalogue des seuils réglementaires suivis par Lettra, en un seul endroit : chaque montant porte sa date d'effet et
/// sa source officielle (Code général des impôts, code des impositions sur les biens et services, Code du travail,
/// BOFiP, impots.gouv.fr). À faire relire par un expert-comptable avant tout usage réel, comme les autres règles fiscales.
/// </summary>
public static class ThresholdCatalog
{
    private const string FranchiseBefore2025 = "CGI, art. 293 B ; BOI-BAREME-000036 (seuils 2023 et 2024)";
    private const string Franchise = "CGI, art. 293 B ; loi de finances pour 2025 ; loi n° 2025-1044 du 3 novembre 2025, qui maintient ces seuils ; impots.gouv.fr, régimes d'imposition à la TVA";
    private const string SimplifiedBefore2026 = "CIBS, art. L. 162-5 ; BOI-BAREME-000036 (seuils 2023 à 2025)";
    private const string Simplified = "CIBS, art. L. 162-5 ; BOI-BAREME-000036 (seuils 2026) ; régime supprimé au 1er janvier 2027 (loi de finances pour 2025, art. 38)";
    private const string Quarterly = "Loi de finances pour 2025, art. 38 ; impots.gouv.fr, « Le régime simplifié d'imposition à la TVA est supprimé à compter du 1er janvier 2027 »";
    private const string Size = "Décret n° 2008-1354 du 18 décembre 2008, art. 3 (catégories d'entreprises)";
    private const string Vigilance = "Code du travail, art. L. 8222-1 et R. 8222-1 (décret n° 2015-364 du 30 mars 2015)";
    private const string FeeReturn = "CGI, art. 240 ; BOI-BIC-DECLA-30-70-20";

    private static readonly DateOnly Always = DateOnly.MinValue;

    public static IReadOnlyList<ThresholdParameter> All { get; } =
    [
        new(ThresholdCodes.FranchiseGoodsBase, 91_900m, ThresholdBasis.ExclTax, new(2023, 1, 1), new(2024, 12, 31), FranchiseBefore2025),
        new(ThresholdCodes.FranchiseGoodsIncreased, 101_000m, ThresholdBasis.ExclTax, new(2023, 1, 1), new(2024, 12, 31), FranchiseBefore2025),
        new(ThresholdCodes.FranchiseServicesBase, 36_800m, ThresholdBasis.ExclTax, new(2023, 1, 1), new(2024, 12, 31), FranchiseBefore2025),
        new(ThresholdCodes.FranchiseServicesIncreased, 39_100m, ThresholdBasis.ExclTax, new(2023, 1, 1), new(2024, 12, 31), FranchiseBefore2025),
        new(ThresholdCodes.FranchiseGoodsBase, 85_000m, ThresholdBasis.ExclTax, new(2025, 1, 1), null, Franchise),
        new(ThresholdCodes.FranchiseGoodsIncreased, 93_500m, ThresholdBasis.ExclTax, new(2025, 1, 1), null, Franchise),
        new(ThresholdCodes.FranchiseServicesBase, 37_500m, ThresholdBasis.ExclTax, new(2025, 1, 1), null, Franchise),
        new(ThresholdCodes.FranchiseServicesIncreased, 41_250m, ThresholdBasis.ExclTax, new(2025, 1, 1), null, Franchise),
        new(ThresholdCodes.SimplifiedGoodsIncreased, 925_000m, ThresholdBasis.ExclTax, new(2023, 1, 1), new(2025, 12, 31), SimplifiedBefore2026),
        new(ThresholdCodes.SimplifiedServicesIncreased, 287_000m, ThresholdBasis.ExclTax, new(2023, 1, 1), new(2025, 12, 31), SimplifiedBefore2026),
        new(ThresholdCodes.SimplifiedGoodsIncreased, 1_040_000m, ThresholdBasis.ExclTax, new(2026, 1, 1), new(2026, 12, 31), Simplified),
        new(ThresholdCodes.SimplifiedServicesIncreased, 323_000m, ThresholdBasis.ExclTax, new(2026, 1, 1), new(2026, 12, 31), Simplified),
        new(ThresholdCodes.QuarterlyBase, 1_000_000m, ThresholdBasis.ExclTax, new(2027, 1, 1), null, Quarterly),
        new(ThresholdCodes.QuarterlyIncreased, 1_100_000m, ThresholdBasis.ExclTax, new(2027, 1, 1), null, Quarterly),
        new(ThresholdCodes.SizeMicro, 2_000_000m, ThresholdBasis.ExclTax, new(2008, 12, 20), null, Size),
        new(ThresholdCodes.SizeSmallOrMedium, 50_000_000m, ThresholdBasis.ExclTax, new(2008, 12, 20), null, Size),
        new(ThresholdCodes.SizeIntermediate, 1_500_000_000m, ThresholdBasis.ExclTax, new(2008, 12, 20), null, Size),
        new(ThresholdCodes.Vigilance, 3_000m, ThresholdBasis.ExclTax, Always, new(2015, 4, 30), "Code du travail, art. R. 8222-1 (avant le décret n° 2015-364)"),
        new(ThresholdCodes.Vigilance, 5_000m, ThresholdBasis.ExclTax, new(2015, 5, 1), null, Vigilance),
        new(ThresholdCodes.FeeReturn, 1_200m, ThresholdBasis.InclTax, Always, new(2023, 12, 31), FeeReturn),
        new(ThresholdCodes.FeeReturn, 2_400m, ThresholdBasis.InclTax, new(2024, 1, 1), null, FeeReturn),
    ];

    /// <summary>Le seuil <paramref name="code"/> en vigueur le <paramref name="date"/>, ou <c>null</c> s'il ne s'applique pas ce jour-là.</summary>
    public static ThresholdParameter? Find(string code, DateOnly date) =>
        All.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.Ordinal) && p.AppliesOn(date));

    /// <summary>Le montant du seuil <paramref name="code"/> le <paramref name="date"/> ; le code et la date doivent exister au catalogue.</summary>
    public static decimal AmountOn(string code, DateOnly date) =>
        Find(code, date)?.Amount ?? throw new ArgumentOutOfRangeException(nameof(code), code, "Seuil absent du catalogue à cette date.");
}
