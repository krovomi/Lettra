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

    public static class Numbering
    {
        public const string SeriesNotFound = "Numbering.SeriesNotFound";
        public const string Contention = "Numbering.Contention";
    }

    /// <summary>Chaîne anti-fraude (journal chaîné, clôtures, archives scellées).</summary>
    public static class Integrity
    {
        public const string EventInvalid = "Integrity.EventInvalid";
        public const string ClosingNotFound = "Integrity.ClosingNotFound";
        public const string KeyUnavailable = "Integrity.KeyUnavailable";
    }

    /// <summary>Centre d'alertes (module Supervision).</summary>
    public static class Alerts
    {
        public const string ScopeIncomplete = "Alert.ScopeIncomplete";
        public const string RuleRequired = "Alert.RuleRequired";
        public const string ReasonRequired = "Alert.ReasonRequired";
        public const string AgentNotAllowed = "Alert.AgentNotAllowed";
        public const string HumanRequired = "Alert.HumanRequired";
        public const string InvalidTransition = "Alert.InvalidTransition";
        public const string SameSeverity = "Alert.SameSeverity";
        public const string NotFound = "Alert.NotFound";
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
