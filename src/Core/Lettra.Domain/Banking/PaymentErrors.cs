using Lettra.Domain.Common;

namespace Lettra.Domain.Banking;

/// <summary>Codes d'erreur des encaissements, du rapprochement bancaire et du lettrage (vague 6).</summary>
public static class PaymentErrors
{
    public const string NotFoundCode = "Payment.NotFound";
    public const string AmountInvalidCode = "Payment.AmountInvalid";
    public const string DateInvalidCode = "Payment.DateInvalid";
    public const string MethodInvalidCode = "Payment.MethodInvalid";
    public const string ReferenceTooLongCode = "Payment.ReferenceTooLong";
    public const string AllocationsRequiredCode = "Payment.AllocationsRequired";
    public const string TooManyAllocationsCode = "Payment.TooManyAllocations";
    public const string AllocationInvalidCode = "Payment.AllocationInvalid";
    public const string AllocationDuplicateCode = "Payment.AllocationDuplicate";
    public const string OtherCustomerInvoiceCode = "Payment.OtherCustomerInvoice";
    public const string GapAboveThresholdCode = "Payment.GapAboveThreshold";
    public const string ExceedsOutstandingCode = "Payment.ExceedsOutstanding";
    public const string CreditNoteNotPayableCode = "Payment.CreditNoteNotPayable";
    public const string AlreadyReversedCode = "Payment.AlreadyReversed";
    public const string ReasonRequiredCode = "Payment.ReasonRequired";
    public const string ReasonTooLongCode = "Payment.ReasonTooLong";
    public const string CustomerNotFoundCode = "Payment.CustomerNotFound";

    public static Error NotFound => Error.NotFound(NotFoundCode);

    public static Error AmountInvalid => Error.Validation(AmountInvalidCode, "Amount");

    public static Error DateInvalid => Error.Validation(DateInvalidCode, "Date");

    public static Error MethodInvalid => Error.Validation(MethodInvalidCode, "Method");

    public static Error ReferenceTooLong => Error.Validation(ReferenceTooLongCode, "Reference");

    public static Error AllocationsRequired => Error.Validation(AllocationsRequiredCode, "Allocations");

    public static Error TooManyAllocations => Error.Validation(TooManyAllocationsCode, "Allocations");

    public static Error AllocationDuplicate => Error.Validation(AllocationDuplicateCode, "Allocations");

    /// <summary>L'écart entre le montant reçu et le total imputé dépasse le seuil réglé pour le dossier.</summary>
    public static Error GapAboveThreshold => Error.Validation(GapAboveThresholdCode, "Amount");

    public static Error ExceedsOutstanding => Error.Validation(ExceedsOutstandingCode, "Allocations");

    /// <summary>Un avoir ne s'encaisse pas : il se lettre avec sa facture.</summary>
    public static Error CreditNoteNotPayable => Error.Conflict(CreditNoteNotPayableCode);

    public static Error AlreadyReversed => Error.Conflict(AlreadyReversedCode);

    public static Error ReasonRequired => Error.Validation(ReasonRequiredCode, "Reason");

    public static Error ReasonTooLong => Error.Validation(ReasonTooLongCode, "Reason");

    public static Error CustomerNotFound => Error.Validation(CustomerNotFoundCode, "CustomerId");

    public static Error AllocationInvalid(int index) => Error.Validation(AllocationInvalidCode, $"Allocations[{index}]");

    public static Error OtherCustomerInvoice(int index) => Error.Validation(OtherCustomerInvoiceCode, $"Allocations[{index}]");
}

/// <summary>Codes d'erreur de l'import d'un relevé bancaire et du rapprochement.</summary>
public static class BankStatementErrors
{
    public const string FileRequiredCode = "BankStatement.FileRequired";
    public const string FileTooLargeCode = "BankStatement.FileTooLarge";
    public const string FormatUnknownCode = "BankStatement.FormatUnknown";
    public const string FileInvalidCode = "BankStatement.FileInvalid";
    public const string NoTransactionsCode = "BankStatement.NoTransactions";
    public const string CsvColumnsInvalidCode = "BankStatement.CsvColumnsInvalid";
    public const string LineNotFoundCode = "BankStatement.LineNotFound";
    public const string LineAlreadyMatchedCode = "BankStatement.LineAlreadyMatched";
    public const string LineNotCreditCode = "BankStatement.LineNotCredit";
    public const string AmountMismatchCode = "BankStatement.AmountMismatch";
    public const string TooManyLinesCode = "BankStatement.TooManyLines";

    public static Error FileRequired => Error.Validation(FileRequiredCode, "File");

    public static Error FileTooLarge => Error.Validation(FileTooLargeCode, "File");

    public static Error FormatUnknown => Error.Validation(FormatUnknownCode, "File");

    /// <summary>Le fichier ne se lit pas : <paramref name="line"/> est le numéro de ligne (CSV, OFX) ou 0.</summary>
    public static Error FileInvalid(int line) => Error.Validation(FileInvalidCode, line > 0 ? $"File[{line}]" : "File");

    public static Error NoTransactions => Error.Validation(NoTransactionsCode, "File");

    public static Error CsvColumnsInvalid => Error.Validation(CsvColumnsInvalidCode, "Csv");

    public static Error LineNotFound => Error.NotFound(LineNotFoundCode);

    public static Error LineAlreadyMatched => Error.Conflict(LineAlreadyMatchedCode);

    /// <summary>Seul un crédit du relevé (argent reçu) devient un encaissement client.</summary>
    public static Error LineNotCredit => Error.Validation(LineNotCreditCode, "BankLineId");

    public static Error AmountMismatch => Error.Validation(AmountMismatchCode, "Amount");

    public static Error TooManyLines => Error.Validation(TooManyLinesCode, "Items");
}

/// <summary>Codes d'erreur des réglages du rapprochement.</summary>
public static class BankingSettingsErrors
{
    public const string ThresholdInvalidCode = "BankingSettings.ThresholdInvalid";

    public static Error ThresholdInvalid => Error.Validation(ThresholdInvalidCode, "WriteOffThreshold");
}
