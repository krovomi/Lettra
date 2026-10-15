namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : devis, factures, avoirs, grilles tarifaires et mentions (phase 3).</summary>
public static partial class ErrorCodes
{
    /// <summary>Devis et factures : règles communes aux pièces (phase 3).</summary>
    public static class Billing
    {
        public const string CustomerRequired = "Billing.CustomerRequired";
        public const string LinesRequired = "Billing.LinesRequired";
        public const string TooManyLines = "Billing.TooManyLines";
        public const string LineReferenceTooLong = "Billing.LineReferenceTooLong";
        public const string LineDesignationRequired = "Billing.LineDesignationRequired";
        public const string LineDesignationTooLong = "Billing.LineDesignationTooLong";
        public const string LineDescriptionTooLong = "Billing.LineDescriptionTooLong";
        public const string LineKindInvalid = "Billing.LineKindInvalid";
        public const string LineQuantityInvalid = "Billing.LineQuantityInvalid";
        public const string LinePriceInvalid = "Billing.LinePriceInvalid";
        public const string LineDiscountInvalid = "Billing.LineDiscountInvalid";
        public const string VatRegimeInvalid = "Billing.VatRegimeInvalid";
        public const string ExemptionUnknown = "Billing.ExemptionUnknown";
        public const string ExemptionOutsideStandardRegime = "Billing.ExemptionOutsideStandardRegime";
        public const string SubjectTooLong = "Billing.SubjectTooLong";
        public const string NotesTooLong = "Billing.NotesTooLong";
        public const string PaymentTermsTooLong = "Billing.PaymentTermsTooLong";
        public const string DeliveryAddressTooLong = "Billing.DeliveryAddressTooLong";
        public const string CreditReasonTooLong = "Billing.CreditReasonTooLong";
        public const string PurchaseOrderReferenceTooLong = "Billing.PurchaseOrderReferenceTooLong";
        public const string DueDateBeforeIssueDate = "Billing.DueDateBeforeIssueDate";
        public const string NotDraft = "Billing.NotDraft";
        public const string NotCompliant = "Billing.NotCompliant";
        public const string CompanyProfileMissing = "Billing.CompanyProfileMissing";
        public const string DownPaymentInvalid = "Billing.DownPaymentInvalid";

        /// <summary>Une pièce validée hors ligne ne se reconstruit pas à l'identique au serveur (totaux, état).</summary>
        public const string ReplicaMismatch = "Billing.ReplicaMismatch";
    }

    public static class Quote
    {
        public const string NotFound = "Quote.NotFound";
        public const string ValidUntilBeforeIssueDate = "Quote.ValidUntilBeforeIssueDate";
        public const string NotValidated = "Quote.NotValidated";
        public const string NotAccepted = "Quote.NotAccepted";
        public const string AlreadyInvoiced = "Quote.AlreadyInvoiced";
        public const string DownPaymentsExceedTotal = "Quote.DownPaymentsExceedTotal";
    }

    public static class Invoice
    {
        public const string NotFound = "Invoice.NotFound";
        public const string NotValidated = "Invoice.NotValidated";
        public const string AlreadyIssued = "Invoice.AlreadyIssued";
        public const string CannotCreditCreditNote = "Invoice.CannotCreditCreditNote";
        public const string CreditLineUnknown = "Invoice.CreditLineUnknown";
        public const string CreditExceedsBalance = "Invoice.CreditExceedsBalance";
        public const string NothingToCredit = "Invoice.NothingToCredit";
        public const string DerivedDocumentLocked = "Invoice.DerivedDocumentLocked";
    }

    public static class PriceList
    {
        public const string NameRequired = "PriceList.NameRequired";
        public const string NameTooLong = "PriceList.NameTooLong";
        public const string EntryInvalid = "PriceList.EntryInvalid";
        public const string EntryDuplicate = "PriceList.EntryDuplicate";
        public const string CatalogItemUnknown = "PriceList.CatalogItemUnknown";
        public const string CustomerUnknown = "PriceList.CustomerUnknown";
        public const string NotFound = "PriceList.NotFound";
    }

    /// <summary>
    /// Mentions obligatoires d'une pièce (checklist de conformité). Un constat bloquant est aussi renvoyé comme
    /// erreur de validation, avec le même code.
    /// </summary>
    public static class Mention
    {
        public const string SellerName = "Mention.SellerName";
        public const string SellerAddress = "Mention.SellerAddress";
        public const string SellerSiren = "Mention.SellerSiren";
        public const string SellerRcs = "Mention.SellerRcs";
        public const string SellerLegalForm = "Mention.SellerLegalForm";
        public const string SellerVatNumber = "Mention.SellerVatNumber";
        public const string CustomerName = "Mention.CustomerName";
        public const string CustomerAddress = "Mention.CustomerAddress";
        public const string CustomerSiren = "Mention.CustomerSiren";
        public const string CustomerVatNumber = "Mention.CustomerVatNumber";
        public const string Number = "Mention.Number";
        public const string IssueDate = "Mention.IssueDate";
        public const string DeliveryDate = "Mention.DeliveryDate";
        public const string OperationCategory = "Mention.OperationCategory";
        public const string DeliveryAddress = "Mention.DeliveryAddress";
        public const string VatOnDebits = "Mention.VatOnDebits";
        public const string Lines = "Mention.Lines";
        public const string LineDesignation = "Mention.LineDesignation";
        public const string LineQuantity = "Mention.LineQuantity";
        public const string LineUnitPrice = "Mention.LineUnitPrice";
        public const string Totals = "Mention.Totals";
        public const string DueDate = "Mention.DueDate";
        public const string EarlyPaymentDiscount = "Mention.EarlyPaymentDiscount";
        public const string LatePenalties = "Mention.LatePenalties";
        public const string RecoveryIndemnity = "Mention.RecoveryIndemnity";
        public const string VatExemptionText = "Mention.VatExemptionText";
        public const string OriginalInvoice = "Mention.OriginalInvoice";
    }
}
