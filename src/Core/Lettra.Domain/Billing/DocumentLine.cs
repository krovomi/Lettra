using Lettra.Calculations;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Billing;

/// <summary>Une ligne saisie, avant contrôle (même forme pour les devis et les factures).</summary>
public sealed record LineDraft(
    Guid? CatalogItemId,
    string? Reference,
    string? Designation,
    string? Description,
    CatalogItemKind Kind,
    string? UnitCode,
    decimal Quantity,
    decimal UnitPriceExclTax,
    decimal DiscountPercent,
    string? VatRateCode,
    string? VatExemptionCode);

/// <summary>
/// Ligne d'un devis ou d'une facture. C'est une copie : elle ne change pas quand l'article du catalogue change.
/// Son montant HT est arrondi au centime par la règle unique de <see cref="Rounding"/>.
/// </summary>
public sealed class DocumentLine
{
    public const int MaxReferenceLength = 50;
    public const int MaxDesignationLength = 200;
    public const int MaxDescriptionLength = 2000;
    public const decimal MaxQuantity = 999_999_999m;

    // Constructeur réservé à la persistance.
    private DocumentLine()
    {
        Designation = string.Empty;
        UnitCode = string.Empty;
        VatRateCode = string.Empty;
        VatCategory = string.Empty;
    }

    private DocumentLine(int position, LineDraft draft, RevenueNature nature, string designation, string unitCode, LineVat vat)
    {
        Position = position;
        CatalogItemId = draft.CatalogItemId;
        Reference = string.IsNullOrWhiteSpace(draft.Reference) ? null : draft.Reference.Trim();
        Designation = designation;
        Description = string.IsNullOrWhiteSpace(draft.Description) ? null : draft.Description.Trim();
        Nature = nature;
        UnitCode = unitCode;
        Quantity = Rounding.ToStorage(draft.Quantity);
        UnitPriceExclTax = Rounding.ToStorage(draft.UnitPriceExclTax);
        DiscountPercent = Rounding.ToStorage(draft.DiscountPercent);
        VatRateCode = vat.Rate.Code;
        VatRatePercent = vat.Rate.Percent;
        VatCategory = vat.Category;
        VatExemptionCode = vat.ExemptionCode;
        TotalExclTax = LineAmounts.TotalExclTax(ToInput());
    }

    /// <summary>Rang de la ligne sur la pièce, à partir de 1.</summary>
    public int Position { get; private set; }

    public Guid? CatalogItemId { get; private set; }

    public string? Reference { get; private set; }

    public string Designation { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Rôle comptable : bien (707), service (706) ou acompte (4191).</summary>
    public RevenueNature Nature { get; private set; }

    public CatalogItemKind Kind => Nature == RevenueNature.Goods ? CatalogItemKind.Good : CatalogItemKind.Service;

    public string UnitCode { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal UnitPriceExclTax { get; private set; }

    public decimal DiscountPercent { get; private set; }

    public string VatRateCode { get; private set; }

    public decimal VatRatePercent { get; private set; }

    /// <summary>Catégorie EN 16931 (<see cref="VatCategoryCodes"/>).</summary>
    public string VatCategory { get; private set; }

    /// <summary>Code VATEX quand la ligne n'est pas taxée.</summary>
    public string? VatExemptionCode { get; private set; }

    public decimal TotalExclTax { get; private set; }

    public LineInput ToInput() => new(Quantity, UnitPriceExclTax, VatRatePercent, DiscountPercent, VatCategory);

    /// <summary>Contrôle une ligne saisie, en accumulant ses erreurs (cible <c>Lines[i].Champ</c>).</summary>
    public static Result<DocumentLine> Create(int index, LineDraft draft, VatRegime regime, RevenueNature? natureOverride = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        List<Error> errors = [];

        string? reference = draft.Reference?.Trim();
        if (reference is { Length: > MaxReferenceLength })
        {
            errors.Add(BillingErrors.Line(BillingErrors.LineReferenceTooLongCode, index, "Reference"));
        }

        string designation = (draft.Designation ?? string.Empty).Trim();
        if (designation.Length == 0)
        {
            errors.Add(BillingErrors.Line(BillingErrors.LineDesignationRequiredCode, index, "Designation"));
        }
        else if (designation.Length > MaxDesignationLength)
        {
            errors.Add(BillingErrors.Line(BillingErrors.LineDesignationTooLongCode, index, "Designation"));
        }

        if (draft.Description is { Length: > MaxDescriptionLength })
        {
            errors.Add(BillingErrors.Line(BillingErrors.LineDescriptionTooLongCode, index, "Description"));
        }

        if (!Enum.IsDefined(draft.Kind))
        {
            errors.Add(BillingErrors.Line(BillingErrors.LineKindInvalidCode, index, "Kind"));
        }

        Result<UnitOfMeasure> unit = UnitOfMeasure.FromCode(draft.UnitCode);
        errors.AddRange(unit.Errors.Select(e => e.For($"Lines[{index}].UnitCode")));

        // Une quantité négative n'existe que sur les lignes produites par Lettra (acompte déduit).
        decimal quantity = Rounding.ToStorage(draft.Quantity);
        if (quantity == 0m || Math.Abs(quantity) > MaxQuantity || (quantity < 0m && natureOverride != RevenueNature.DownPayment))
        {
            errors.Add(BillingErrors.Line(BillingErrors.LineQuantityInvalidCode, index, "Quantity"));
        }

        if (draft.UnitPriceExclTax < 0m || draft.UnitPriceExclTax > CatalogItem.MaxUnitPrice)
        {
            errors.Add(BillingErrors.Line(BillingErrors.LinePriceInvalidCode, index, "UnitPriceExclTax"));
        }

        if (draft.DiscountPercent is < 0m or > 100m)
        {
            errors.Add(BillingErrors.Line(BillingErrors.LineDiscountInvalidCode, index, "DiscountPercent"));
        }

        Result<LineVat> vat = VatTreatment.Resolve(regime, draft.VatRateCode, draft.VatExemptionCode);
        errors.AddRange(vat.Errors.Select(e => e.For(
            e.Code == SharedKernelErrors.VatRateUnknownCode ? $"Lines[{index}].VatRateCode" : $"Lines[{index}].VatExemptionCode")));

        if (errors.Count > 0)
        {
            return Result<DocumentLine>.Failure(errors);
        }

        RevenueNature nature = natureOverride ?? (draft.Kind == CatalogItemKind.Good ? RevenueNature.Goods : RevenueNature.Services);
        return new DocumentLine(index + 1, draft with { Quantity = quantity }, nature, designation, unit.Value.Code, vat.Value);
    }

    /// <summary>Copie de la ligne pour une autre pièce (avoir, facture finale), avec une autre quantité au besoin.</summary>
    public LineDraft ToDraft(decimal? quantity = null) => new(
        CatalogItemId, Reference, Designation, Description, Kind, UnitCode, quantity ?? Quantity, UnitPriceExclTax, DiscountPercent,
        VatRateCode, VatCategory == VatCategoryCodes.Exempt ? VatExemptionCode : null);
}
