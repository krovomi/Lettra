using Lettra.Domain.Common;

namespace Lettra.Domain.Billing;

/// <summary>Codes d'erreur communs aux devis et aux factures.</summary>
public static class BillingErrors
{
    public const string CustomerRequiredCode = "Billing.CustomerRequired";
    public const string LinesRequiredCode = "Billing.LinesRequired";
    public const string TooManyLinesCode = "Billing.TooManyLines";
    public const string LineReferenceTooLongCode = "Billing.LineReferenceTooLong";
    public const string LineDesignationRequiredCode = "Billing.LineDesignationRequired";
    public const string LineDesignationTooLongCode = "Billing.LineDesignationTooLong";
    public const string LineDescriptionTooLongCode = "Billing.LineDescriptionTooLong";
    public const string LineKindInvalidCode = "Billing.LineKindInvalid";
    public const string LineQuantityInvalidCode = "Billing.LineQuantityInvalid";
    public const string LinePriceInvalidCode = "Billing.LinePriceInvalid";
    public const string LineDiscountInvalidCode = "Billing.LineDiscountInvalid";
    public const string VatRegimeInvalidCode = "Billing.VatRegimeInvalid";
    public const string ExemptionUnknownCode = "Billing.ExemptionUnknown";
    public const string ExemptionOutsideStandardRegimeCode = "Billing.ExemptionOutsideStandardRegime";
    public const string SubjectTooLongCode = "Billing.SubjectTooLong";
    public const string NotesTooLongCode = "Billing.NotesTooLong";
    public const string PaymentTermsTooLongCode = "Billing.PaymentTermsTooLong";
    public const string DeliveryAddressTooLongCode = "Billing.DeliveryAddressTooLong";
    public const string CreditReasonTooLongCode = "Billing.CreditReasonTooLong";
    public const string PurchaseOrderReferenceTooLongCode = "Billing.PurchaseOrderReferenceTooLong";
    public const string DueDateBeforeIssueDateCode = "Billing.DueDateBeforeIssueDate";
    public const string NotDraftCode = "Billing.NotDraft";
    public const string NotCompliantCode = "Billing.NotCompliant";
    public const string CompanyProfileMissingCode = "Billing.CompanyProfileMissing";
    public const string DownPaymentInvalidCode = "Billing.DownPaymentInvalid";
    public const string ReplicaMismatchCode = "Billing.ReplicaMismatch";

    public static Error CustomerRequired => Error.Validation(CustomerRequiredCode, "CustomerId");
    public static Error LinesRequired => Error.Validation(LinesRequiredCode, "Lines");
    public static Error TooManyLines => Error.Validation(TooManyLinesCode, "Lines");
    public static Error VatRegimeInvalid => Error.Validation(VatRegimeInvalidCode, "VatRegime");
    public static Error SubjectTooLong => Error.Validation(SubjectTooLongCode, "Subject");
    public static Error NotesTooLong => Error.Validation(NotesTooLongCode, "Notes");
    public static Error PaymentTermsTooLong => Error.Validation(PaymentTermsTooLongCode, "PaymentTerms");
    public static Error DeliveryAddressTooLong => Error.Validation(DeliveryAddressTooLongCode, "DeliveryAddress");
    public static Error CreditReasonTooLong => Error.Validation(CreditReasonTooLongCode, "Reason");
    public static Error PurchaseOrderReferenceTooLong => Error.Validation(PurchaseOrderReferenceTooLongCode, "PurchaseOrderReference");
    public static Error DueDateBeforeIssueDate => Error.Validation(DueDateBeforeIssueDateCode, "DueDate");

    /// <summary>Une pièce validée est figée : elle ne se corrige que par un avoir.</summary>
    public static Error NotDraft => Error.Conflict(NotDraftCode);

    public static Error NotCompliant => Error.Validation(NotCompliantCode);

    /// <summary>Une pièce validée ailleurs (poste hors ligne) ne se reconstruit pas à l'identique : totaux ou état divergents.</summary>
    public static Error ReplicaMismatch => Error.Validation(ReplicaMismatchCode);

    public static Error CompanyProfileMissing => Error.Conflict(CompanyProfileMissingCode);

    public static Error DownPaymentInvalid => Error.Validation(DownPaymentInvalidCode, "Percent");

    internal static Error Line(string code, int index, string field) => Error.Validation(code, $"Lines[{index}].{field}");
}

public static class QuoteErrors
{
    public const string NotFoundCode = "Quote.NotFound";
    public const string ValidUntilBeforeIssueDateCode = "Quote.ValidUntilBeforeIssueDate";
    public const string NotValidatedCode = "Quote.NotValidated";
    public const string NotAcceptedCode = "Quote.NotAccepted";
    public const string AlreadyInvoicedCode = "Quote.AlreadyInvoiced";
    public const string DownPaymentsExceedTotalCode = "Quote.DownPaymentsExceedTotal";

    public static Error NotFound => Error.NotFound(NotFoundCode);
    public static Error ValidUntilBeforeIssueDate => Error.Validation(ValidUntilBeforeIssueDateCode, "ValidUntil");
    public static Error NotValidated => Error.Conflict(NotValidatedCode);
    public static Error NotAccepted => Error.Conflict(NotAcceptedCode);
    public static Error AlreadyInvoiced => Error.Conflict(AlreadyInvoicedCode);
    public static Error DownPaymentsExceedTotal => Error.Validation(DownPaymentsExceedTotalCode, "AmountInclTax");
}

public static class InvoiceErrors
{
    public const string NotFoundCode = "Invoice.NotFound";
    public const string NotValidatedCode = "Invoice.NotValidated";
    public const string AlreadyIssuedCode = "Invoice.AlreadyIssued";
    public const string CannotCreditCreditNoteCode = "Invoice.CannotCreditCreditNote";
    public const string CreditLineUnknownCode = "Invoice.CreditLineUnknown";
    public const string CreditExceedsBalanceCode = "Invoice.CreditExceedsBalance";
    public const string NothingToCreditCode = "Invoice.NothingToCredit";
    public const string DerivedDocumentLockedCode = "Invoice.DerivedDocumentLocked";

    public static Error NotFound => Error.NotFound(NotFoundCode);
    public static Error NotValidated => Error.Conflict(NotValidatedCode);
    public static Error AlreadyIssued => Error.Conflict(AlreadyIssuedCode);
    public static Error CannotCreditCreditNote => Error.Conflict(CannotCreditCreditNoteCode);
    public static Error CreditLineUnknown => Error.Validation(CreditLineUnknownCode, "Lines");
    public static Error CreditExceedsBalance => Error.Validation(CreditExceedsBalanceCode, "Lines");
    public static Error NothingToCredit => Error.Conflict(NothingToCreditCode);
    public static Error DerivedDocumentLocked => Error.Conflict(DerivedDocumentLockedCode);
}
