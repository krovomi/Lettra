namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : module Catalogue.</summary>
public static partial class ErrorCodes
{
    public static class CatalogItem
    {
        public const string ReferenceRequired = "CatalogItem.ReferenceRequired";
        public const string ReferenceTooLong = "CatalogItem.ReferenceTooLong";
        public const string ReferenceInvalid = "CatalogItem.ReferenceInvalid";
        public const string ReferenceAlreadyUsed = "CatalogItem.ReferenceAlreadyUsed";
        public const string DesignationRequired = "CatalogItem.DesignationRequired";
        public const string DesignationTooLong = "CatalogItem.DesignationTooLong";
        public const string DescriptionTooLong = "CatalogItem.DescriptionTooLong";
        public const string KindInvalid = "CatalogItem.KindInvalid";
        public const string UnitUnknown = "CatalogItem.UnitUnknown";
        public const string PriceNegative = "CatalogItem.PriceNegative";
        public const string PriceTooLarge = "CatalogItem.PriceTooLarge";
        public const string Archived = "CatalogItem.Archived";
        public const string NotFound = "CatalogItem.NotFound";
    }
}
