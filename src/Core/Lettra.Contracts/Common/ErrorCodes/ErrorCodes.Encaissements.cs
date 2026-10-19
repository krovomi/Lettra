namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : encaissements, relevés bancaires, rapprochement et lettrage (vague 6, module Exports).</summary>
public static partial class ErrorCodes
{
    public static class Payment
    {
        public const string NotFound = "Payment.NotFound";
        public const string AmountInvalid = "Payment.AmountInvalid";
        public const string DateInvalid = "Payment.DateInvalid";
        public const string MethodInvalid = "Payment.MethodInvalid";
        public const string ReferenceTooLong = "Payment.ReferenceTooLong";
        public const string AllocationsRequired = "Payment.AllocationsRequired";
        public const string TooManyAllocations = "Payment.TooManyAllocations";
        public const string AllocationInvalid = "Payment.AllocationInvalid";
        public const string AllocationDuplicate = "Payment.AllocationDuplicate";
        public const string OtherCustomerInvoice = "Payment.OtherCustomerInvoice";
        public const string GapAboveThreshold = "Payment.GapAboveThreshold";
        public const string ExceedsOutstanding = "Payment.ExceedsOutstanding";
        public const string CreditNoteNotPayable = "Payment.CreditNoteNotPayable";
        public const string AlreadyReversed = "Payment.AlreadyReversed";
        public const string ReasonRequired = "Payment.ReasonRequired";
        public const string ReasonTooLong = "Payment.ReasonTooLong";
        public const string CustomerNotFound = "Payment.CustomerNotFound";
    }

    public static class BankStatement
    {
        public const string FileRequired = "BankStatement.FileRequired";
        public const string FileTooLarge = "BankStatement.FileTooLarge";
        public const string FormatUnknown = "BankStatement.FormatUnknown";
        public const string FileInvalid = "BankStatement.FileInvalid";
        public const string NoTransactions = "BankStatement.NoTransactions";
        public const string CsvColumnsInvalid = "BankStatement.CsvColumnsInvalid";
        public const string LineNotFound = "BankStatement.LineNotFound";
        public const string LineAlreadyMatched = "BankStatement.LineAlreadyMatched";
        public const string LineNotCredit = "BankStatement.LineNotCredit";
        public const string AmountMismatch = "BankStatement.AmountMismatch";
        public const string TooManyLines = "BankStatement.TooManyLines";
    }

    public static class BankingSettings
    {
        public const string ThresholdInvalid = "BankingSettings.ThresholdInvalid";
    }
}
