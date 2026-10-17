namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : exports (module Exports, phase 5) ; le FEC, son validateur intégré, la facture électronique et la plateforme agréée.</summary>
public static partial class ErrorCodes
{
    public static class Fec
    {
        // Demande d'export.
        public const string PeriodInvalid = "Fec.PeriodInvalid";
        public const string PeriodTooLong = "Fec.PeriodTooLong";
        public const string SirenMissing = "Fec.SirenMissing";
        public const string ExportInvalid = "Fec.ExportInvalid";
        public const string FileTooLarge = "Fec.FileTooLarge";

        // Constats du validateur.
        public const string FileNameInvalid = "Fec.FileNameInvalid";
        public const string Empty = "Fec.Empty";
        public const string HeaderInvalid = "Fec.HeaderInvalid";
        public const string SeparatorInvalid = "Fec.SeparatorInvalid";
        public const string ColumnCountInvalid = "Fec.ColumnCountInvalid";
        public const string FieldRequired = "Fec.FieldRequired";
        public const string DateInvalid = "Fec.DateInvalid";
        public const string AmountInvalid = "Fec.AmountInvalid";
        public const string DebitAndCredit = "Fec.DebitAndCredit";
        public const string ZeroLine = "Fec.ZeroLine";
        public const string AccountInvalid = "Fec.AccountInvalid";
        public const string AuxiliaryIncomplete = "Fec.AuxiliaryIncomplete";
        public const string LetteringIncomplete = "Fec.LetteringIncomplete";
        public const string CurrencyIncomplete = "Fec.CurrencyIncomplete";
        public const string EntryUnbalanced = "Fec.EntryUnbalanced";
        public const string EntryInconsistent = "Fec.EntryInconsistent";
        public const string FileUnbalanced = "Fec.FileUnbalanced";
        public const string NotChronological = "Fec.NotChronological";
        public const string ValidationBeforeEntry = "Fec.ValidationBeforeEntry";
        public const string DateOutsidePeriod = "Fec.DateOutsidePeriod";
        public const string NoEntries = "Fec.NoEntries";
    }

    public static class EInvoice
    {
        public const string NotValidated = "EInvoice.NotValidated";
        public const string FormatUnknown = "EInvoice.FormatUnknown";
    }

    public static class Platform
    {
        public const string NotConfigured = "Platform.NotConfigured";
        public const string Unavailable = "Platform.Unavailable";
        public const string SellerSirenMissing = "Platform.SellerSirenMissing";
        public const string NotTransmitted = "Platform.NotTransmitted";
        public const string AlreadyFinal = "Platform.AlreadyFinal";
        public const string PaymentAmountInvalid = "Platform.PaymentAmountInvalid";
        public const string PaymentDateInvalid = "Platform.PaymentDateInvalid";
        public const string ReferenceUnknown = "Platform.ReferenceUnknown";
        public const string Queued = "Platform.Queued";
    }

    /// <summary>Consultation de la comptabilité (journal des ventes, balance, grand livre).</summary>
    public static class Accounting
    {
        public const string PeriodInvalid = "Accounting.PeriodInvalid";
        public const string PeriodTooLong = "Accounting.PeriodTooLong";
        public const string AccountInvalid = "Accounting.AccountInvalid";
    }
}
