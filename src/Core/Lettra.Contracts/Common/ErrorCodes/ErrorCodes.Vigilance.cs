namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : dossier anti-blanchiment du client (LCB-FT).</summary>
public static partial class ErrorCodes
{
    public static class Vigilance
    {
        public const string EvidenceInvalid = "Vigilance.EvidenceInvalid";
        public const string DateInFuture = "Vigilance.DateInFuture";
        public const string TooManyOwners = "Vigilance.TooManyOwners";
        public const string OwnerNameInvalid = "Vigilance.OwnerNameInvalid";
        public const string OwnerShareInvalid = "Vigilance.OwnerShareInvalid";
        public const string SharesOver100 = "Vigilance.SharesOver100";
        public const string LevelInvalid = "Vigilance.LevelInvalid";
        public const string LevelTooLow = "Vigilance.LevelTooLow";
        public const string FactorsInvalid = "Vigilance.FactorsInvalid";
        public const string JustificationRequired = "Vigilance.JustificationRequired";
        public const string JustificationTooLong = "Vigilance.JustificationTooLong";
    }
}
