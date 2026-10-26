namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : attestations de vigilance des fournisseurs.</summary>
public static partial class ErrorCodes
{
    public static class Attestation
    {
        public const string SupplierNotFound = "Attestation.SupplierNotFound";
        public const string AmountInvalid = "Attestation.AmountInvalid";
        public const string DateInFuture = "Attestation.DateInFuture";
        public const string CodeInvalid = "Attestation.CodeInvalid";
        public const string VerificationNeedsCode = "Attestation.VerificationNeedsCode";
        public const string VerifiedBeforeIssued = "Attestation.VerifiedBeforeIssued";
    }
}
