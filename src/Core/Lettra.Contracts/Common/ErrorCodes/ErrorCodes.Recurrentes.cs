namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : factures récurrentes (vague 7).</summary>
public static partial class ErrorCodes
{
    public static class RecurringInvoice
    {
        public const string NotFound = "RecurringInvoice.NotFound";
        public const string NameRequired = "RecurringInvoice.NameRequired";
        public const string NameTooLong = "RecurringInvoice.NameTooLong";
        public const string FrequencyInvalid = "RecurringInvoice.FrequencyInvalid";
        public const string TimingInvalid = "RecurringInvoice.TimingInvalid";
        public const string EndBeforeStart = "RecurringInvoice.EndBeforeStart";
        public const string RevisionInvalid = "RecurringInvoice.RevisionInvalid";
        public const string RevisionPercentInvalid = "RecurringInvoice.RevisionPercentInvalid";
        public const string SyntecIndexInvalid = "RecurringInvoice.SyntecIndexInvalid";
        public const string ScheduleLocked = "RecurringInvoice.ScheduleLocked";
        public const string NothingDue = "RecurringInvoice.NothingDue";
    }
}
