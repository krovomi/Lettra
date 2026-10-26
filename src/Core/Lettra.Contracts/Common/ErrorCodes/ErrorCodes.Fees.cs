namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : déclaration des honoraires (DAS 2).</summary>
public static partial class ErrorCodes
{
    public static class Fee
    {
        public const string SupplierNotFound = "Fee.SupplierNotFound";
        public const string BeneficiaryNotFound = "Fee.BeneficiaryNotFound";
        public const string NatureInvalid = "Fee.NatureInvalid";
        public const string ProfessionRequired = "Fee.ProfessionRequired";
        public const string ProfessionTooLong = "Fee.ProfessionTooLong";
        public const string AmountInvalid = "Fee.AmountInvalid";
        public const string YearInvalid = "Fee.YearInvalid";
    }
}
