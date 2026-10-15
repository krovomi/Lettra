using Lettra.Calculations;
using Lettra.Calculations.Compliance;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Billing;

/// <summary>Régime de TVA d'une pièce (valeurs identiques à <c>VatRegimeDto</c>).</summary>
public enum VatRegime
{
    Standard = 1,
    Franchise = 2,
    ReverseChargeIntraEu = 3,
    ReverseChargeConstruction = 4,
    IntraEuGoodsSupply = 5,
    Export = 6,
}

/// <summary>
/// Motif d'exonération : code VATEX (liste CEF, champ EN 16931 BT-121) et texte légal imprimé sur la pièce.
/// </summary>
public sealed record VatExemptionReason(string Code, string LegalText);

/// <summary>Ce que le régime et la saisie donnent pour une ligne : catégorie EN 16931, taux, motif.</summary>
public readonly record struct LineVat(string Category, VatRate Rate, string? ExemptionCode);

/// <summary>
/// Règles de TVA des pièces : passage du régime à la catégorie des lignes, motifs d'exonération et mentions légales.
/// Les codes VATEX et les textes sont <b>à relire par l'expert-comptable</b> (jalon J2).
/// </summary>
public static class VatTreatment
{
    public const string FranchiseExemption = "VATEX-FR-FRANCHISE";
    public const string ReverseChargeExemption = "VATEX-EU-AE";
    public const string IntraCommunityExemption = "VATEX-EU-IC";
    public const string ExportExemption = "VATEX-EU-G";

    /// <summary>Motifs d'exonération proposés ligne à ligne en régime normal.</summary>
    public static IReadOnlyList<VatExemptionReason> LineExemptions { get; } =
    [
        new("VATEX-EU-132-1I", "Exonération de TVA, article 261-4-4° a du CGI"),
        new("VATEX-EU-132-1C", "Exonération de TVA, article 261-4-1° du CGI"),
        new("VATEX-EU-135-1L", "Exonération de TVA, article 261 D-2° du CGI"),
        new("VATEX-EU-79-C", "Débours non soumis à la TVA, article 267 II-2° du CGI"),
    ];

    public static Result<VatExemptionReason> FindLineExemption(string? code) =>
        LineExemptions.FirstOrDefault(e => string.Equals(e.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase)) is { } reason
            ? reason
            : Error.Validation(BillingErrors.ExemptionUnknownCode);

    /// <summary>Catégorie imposée aux lignes par un régime autre que le régime normal.</summary>
    public static (string Category, string Exemption, string LegalText)? RegimeTreatment(VatRegime regime) => regime switch
    {
        VatRegime.Franchise => (VatCategoryCodes.OutOfScope, FranchiseExemption, MandatoryMentions.SpecialText("franchise_tva")),
        VatRegime.ReverseChargeIntraEu => (VatCategoryCodes.ReverseCharge, ReverseChargeExemption, MandatoryMentions.SpecialText("autoliquidation_intra_ue")),
        VatRegime.ReverseChargeConstruction => (VatCategoryCodes.ReverseCharge, ReverseChargeExemption, MandatoryMentions.SpecialText("sous_traitance_btp")),
        VatRegime.IntraEuGoodsSupply => (VatCategoryCodes.IntraCommunity, IntraCommunityExemption, MandatoryMentions.SpecialText("livraison_intra_ue")),
        VatRegime.Export => (VatCategoryCodes.Export, ExportExemption, MandatoryMentions.SpecialText("export")),
        _ => null,
    };

    /// <summary>
    /// TVA d'une ligne : hors régime normal, le régime l'impose (taux 0) ; en régime normal, un motif d'exonération met la
    /// ligne en catégorie E à 0 %, sinon le taux saisi donne S (ou Z pour 0 %).
    /// </summary>
    public static Result<LineVat> Resolve(VatRegime regime, string? vatRateCode, string? exemptionCode)
    {
        if (RegimeTreatment(regime) is { } imposed)
        {
            return string.IsNullOrWhiteSpace(exemptionCode)
                ? new LineVat(imposed.Category, VatRate.Zero, imposed.Exemption)
                : Error.Validation(BillingErrors.ExemptionOutsideStandardRegimeCode);
        }

        if (!string.IsNullOrWhiteSpace(exemptionCode))
        {
            Result<VatExemptionReason> reason = FindLineExemption(exemptionCode);
            return reason.IsSuccess
                ? new LineVat(VatCategoryCodes.Exempt, VatRate.Zero, reason.Value.Code)
                : Result<LineVat>.Failure(reason.Errors);
        }

        Result<VatRate> rate = VatRate.FromCode(vatRateCode);
        return rate.IsSuccess
            ? new LineVat(rate.Value.Percent == 0m ? VatCategoryCodes.ZeroRated : VatCategoryCodes.Standard, rate.Value, null)
            : Result<LineVat>.Failure(rate.Errors);
    }

    /// <summary>Texte légal d'un motif (régime ou ligne), ou <c>null</c> pour un code inconnu.</summary>
    public static string? LegalTextOf(VatRegime regime, string? exemptionCode)
    {
        if (exemptionCode is null)
        {
            return null;
        }

        return RegimeTreatment(regime) is { } imposed && imposed.Exemption == exemptionCode
            ? imposed.LegalText
            : LineExemptions.FirstOrDefault(e => e.Code == exemptionCode)?.LegalText;
    }
}
