namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : vue cabinet et revue de dossier.</summary>
public static partial class ErrorCodes
{
    public static class Review
    {
        public const string PeriodInvalid = "Review.PeriodInvalid";
        public const string ItemUnknown = "Review.ItemUnknown";
        public const string NoteTooLong = "Review.NoteTooLong";
    }
}
