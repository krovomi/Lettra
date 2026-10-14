namespace Lettra.Contracts.Common;

/// <summary>
/// Catalogue des codes d'erreur visibles par les clients. Le serveur n'envoie jamais de texte
/// localisé : l'UI traduit ces codes (ressources <c>Error.&lt;code&gt;</c>).
/// Un test d'architecture vérifie que chaque code émis par le domaine ou l'application figure
/// ici et possède une traduction française et anglaise.
/// </summary>
public static class ErrorCodes
{
    public static class Validation
    {
        public const string Required = "Validation.Required";
        public const string TooLong = "Validation.TooLong";
        public const string Invalid = "Validation.Invalid";
    }

    public static class SharedKernel
    {
        public const string SirenInvalid = "Siren.Invalid";
        public const string SiretInvalid = "Siret.Invalid";
        public const string VatNumberInvalid = "VatNumber.Invalid";
        public const string EmailInvalid = "Email.Invalid";
        public const string CountryCodeInvalid = "CountryCode.Invalid";
        public const string PostalCodeInvalid = "Address.PostalCodeInvalid";
        public const string AddressLineRequired = "Address.LineRequired";
        public const string CityRequired = "Address.CityRequired";
        public const string CurrencyMismatch = "Money.CurrencyMismatch";
        public const string VatRateUnknown = "VatRate.Unknown";
        public const string IbanInvalid = "Iban.Invalid";
    }

    public static class Customer
    {
        public const string NameRequired = "Customer.NameRequired";
        public const string NameTooLong = "Customer.NameTooLong";
        public const string SirenRequired = "Customer.SirenRequired";
        public const string SiretSirenMismatch = "Customer.SiretSirenMismatch";
        public const string IndividualCannotHaveCompanyIds = "Customer.IndividualCannotHaveCompanyIds";
        public const string KindInvalid = "Customer.KindInvalid";
        public const string NotFound = "Customer.NotFound";
    }

    public static class Party
    {
        public const string VatNumberSirenMismatch = "Party.VatNumberSirenMismatch";
    }

    public static class Supplier
    {
        public const string NameRequired = "Supplier.NameRequired";
        public const string NameTooLong = "Supplier.NameTooLong";
        public const string SirenRequired = "Supplier.SirenRequired";
        public const string SiretSirenMismatch = "Supplier.SiretSirenMismatch";
        public const string NotFound = "Supplier.NotFound";
    }

    public static class CatalogItem
    {
        public const string ReferenceRequired = "CatalogItem.ReferenceRequired";
        public const string ReferenceTooLong = "CatalogItem.ReferenceTooLong";
        public const string ReferenceInvalid = "CatalogItem.ReferenceInvalid";
        public const string ReferenceAlreadyUsed = "CatalogItem.ReferenceAlreadyUsed";
        public const string DesignationRequired = "CatalogItem.DesignationRequired";
        public const string DesignationTooLong = "CatalogItem.DesignationTooLong";
        public const string DescriptionTooLong = "CatalogItem.DescriptionTooLong";
        public const string KindInvalid = "CatalogItem.KindInvalid";
        public const string UnitUnknown = "CatalogItem.UnitUnknown";
        public const string PriceNegative = "CatalogItem.PriceNegative";
        public const string PriceTooLarge = "CatalogItem.PriceTooLarge";
        public const string Archived = "CatalogItem.Archived";
        public const string NotFound = "CatalogItem.NotFound";
    }

    /// <summary>Annuaires publics consultés depuis Lettra (SIRENE, VIES).</summary>
    public static class Directory
    {
        public const string QueryTooShort = "Directory.QueryTooShort";
        public const string Unavailable = "Directory.Unavailable";
        public const string VatCountryNotSupported = "Directory.VatCountryNotSupported";
    }

    public static class Security
    {
        public const string Unauthenticated = "Security.Unauthenticated";
        public const string Forbidden = "Security.Forbidden";
        public const string MfaRequired = "Security.MfaRequired";
        public const string CsrfHeaderMissing = "Security.CsrfHeaderMissing";
    }

    public static class LocalAccount
    {
        public const string AlreadyExists = "LocalAccount.AlreadyExists";
        public const string NotFound = "LocalAccount.NotFound";
        public const string InvalidPassphrase = "LocalAccount.InvalidPassphrase";
        public const string LockedOut = "LocalAccount.LockedOut";
        public const string PassphraseTooShort = "LocalAccount.PassphraseTooShort";
        public const string PassphraseMismatch = "LocalAccount.PassphraseMismatch";
        public const string NotSupported = "LocalAccount.NotSupported";
    }

    public static class Tenancy
    {
        public const string DossierRequired = "Dossier.Required";
        public const string DossierNotFound = "Dossier.NotFound";
        public const string DossierMismatch = "Dossier.Mismatch";
        public const string DossierNameRequired = "Dossier.NameRequired";
        public const string DossierNameTooLong = "Dossier.NameTooLong";
        public const string OrganisationNotFound = "Organisation.NotFound";
        public const string NoCapacity = "Dossier.NoCapacity";
        public const string ProvisioningFailed = "Dossier.ProvisioningFailed";
    }

    public static class Rights
    {
        public const string NotSupported = "Rights.NotSupported";
        public const string UserNotFound = "Rights.UserNotFound";
        public const string GroupNotFound = "Rights.GroupNotFound";
        public const string GroupNameRequired = "Rights.GroupNameRequired";
        public const string GroupNameTooLong = "Rights.GroupNameTooLong";
        public const string SubjectNotFound = "Rights.SubjectNotFound";
        public const string RoleUnknown = "Rights.RoleUnknown";
        public const string ScopeNotFound = "Rights.ScopeNotFound";
        public const string ScopeTooWide = "Rights.ScopeTooWide";
        public const string ExpiryRequired = "Rights.ExpiryRequired";
        public const string ExpiryInPast = "Rights.ExpiryInPast";
        public const string AssignmentNotFound = "Rights.AssignmentNotFound";
        public const string StateInvalid = "Rights.StateInvalid";
    }

    public static class Numbering
    {
        public const string SeriesNotFound = "Numbering.SeriesNotFound";
        public const string Contention = "Numbering.Contention";
    }

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
        public const string DueDateBeforeIssueDate = "Billing.DueDateBeforeIssueDate";
        public const string NotDraft = "Billing.NotDraft";
        public const string VersionMismatch = "Billing.VersionMismatch";
        public const string NotCompliant = "Billing.NotCompliant";
        public const string CompanyProfileMissing = "Billing.CompanyProfileMissing";
        public const string DownPaymentInvalid = "Billing.DownPaymentInvalid";
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

    public static class CompanyProfile
    {
        public const string NameRequired = "CompanyProfile.NameRequired";
        public const string NameTooLong = "CompanyProfile.NameTooLong";
        public const string PaymentDaysInvalid = "CompanyProfile.PaymentDaysInvalid";
        public const string NotFound = "CompanyProfile.NotFound";
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

    public static class Ai
    {
        public const string Disabled = "Ai.Disabled";
        public const string FeatureDisabled = "Ai.FeatureDisabled";
        public const string DossierExcluded = "Ai.DossierExcluded";
        public const string EngineUnavailable = "Ai.EngineUnavailable";
        public const string Refused = "Ai.Refused";
        public const string ToolRoundsExceeded = "Ai.ToolRoundsExceeded";
        public const string Truncated = "Ai.Truncated";
        public const string ToolRejected = "Ai.ToolRejected";
        public const string UngroundedAmounts = "Ai.UngroundedAmounts";
        public const string UnknownReferences = "Ai.UnknownReferences";
        public const string SkillPackStale = "Ai.SkillPackStale";
    }

    public static class Api
    {
        public const string IdempotencyKeyReused = "Api.IdempotencyKeyReused";
        public const string Unexpected = "Api.Unexpected";
        public const string Unreachable = "Api.Unreachable";
    }
}
