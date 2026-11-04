namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : radar de prévention des difficultés (côté cabinet).</summary>
public static partial class ErrorCodes
{
    public static class Prevention
    {
        public const string HorizonInvalid = "Prevention.HorizonInvalid";
        public const string CashBalanceInvalid = "Prevention.CashBalanceInvalid";
        public const string CashBalanceDateInvalid = "Prevention.CashBalanceDateInvalid";
        public const string ThresholdInvalid = "Prevention.ThresholdInvalid";
        public const string SignalUnknown = "Prevention.SignalUnknown";
        public const string RecipientRequired = "Prevention.RecipientRequired";
        public const string SubjectRequired = "Prevention.SubjectRequired";
        public const string SubjectTooLong = "Prevention.SubjectTooLong";
        public const string BodyRequired = "Prevention.BodyRequired";
        public const string BodyTooLong = "Prevention.BodyTooLong";
    }
}
