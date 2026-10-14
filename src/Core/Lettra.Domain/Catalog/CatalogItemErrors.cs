using Lettra.Domain.Common;

namespace Lettra.Domain.Catalog;

/// <summary>Codes d'erreur du module Catalogue.</summary>
public static class CatalogItemErrors
{
    public const string ReferenceRequiredCode = "CatalogItem.ReferenceRequired";
    public const string ReferenceTooLongCode = "CatalogItem.ReferenceTooLong";
    public const string ReferenceInvalidCode = "CatalogItem.ReferenceInvalid";
    public const string ReferenceAlreadyUsedCode = "CatalogItem.ReferenceAlreadyUsed";
    public const string DesignationRequiredCode = "CatalogItem.DesignationRequired";
    public const string DesignationTooLongCode = "CatalogItem.DesignationTooLong";
    public const string DescriptionTooLongCode = "CatalogItem.DescriptionTooLong";
    public const string KindInvalidCode = "CatalogItem.KindInvalid";
    public const string UnitUnknownCode = "CatalogItem.UnitUnknown";
    public const string PriceNegativeCode = "CatalogItem.PriceNegative";
    public const string PriceTooLargeCode = "CatalogItem.PriceTooLarge";
    public const string ArchivedCode = "CatalogItem.Archived";
    public const string NotFoundCode = "CatalogItem.NotFound";

    public static Error ReferenceRequired => Error.Validation(ReferenceRequiredCode, nameof(CatalogItem.Reference));
    public static Error ReferenceTooLong => Error.Validation(ReferenceTooLongCode, nameof(CatalogItem.Reference));
    public static Error ReferenceInvalid => Error.Validation(ReferenceInvalidCode, nameof(CatalogItem.Reference));
    public static Error ReferenceAlreadyUsed => new(ReferenceAlreadyUsedCode, ErrorKind.Conflict, nameof(CatalogItem.Reference));
    public static Error DesignationRequired => Error.Validation(DesignationRequiredCode, nameof(CatalogItem.Designation));
    public static Error DesignationTooLong => Error.Validation(DesignationTooLongCode, nameof(CatalogItem.Designation));
    public static Error DescriptionTooLong => Error.Validation(DescriptionTooLongCode, nameof(CatalogItem.Description));
    public static Error KindInvalid => Error.Validation(KindInvalidCode, nameof(CatalogItem.Kind));
    public static Error UnitUnknown => Error.Validation(UnitUnknownCode, nameof(CatalogItem.Unit));
    public static Error PriceNegative => Error.Validation(PriceNegativeCode, nameof(CatalogItem.UnitPriceExclTax));
    public static Error PriceTooLarge => Error.Validation(PriceTooLargeCode, nameof(CatalogItem.UnitPriceExclTax));
    public static Error Archived => Error.Conflict(ArchivedCode);
    public static Error NotFound => Error.NotFound(NotFoundCode);
}
