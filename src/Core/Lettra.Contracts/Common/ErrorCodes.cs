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

    public static class Security
    {
        public const string Unauthenticated = "Security.Unauthenticated";
        public const string Forbidden = "Security.Forbidden";
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

    public static class Api
    {
        public const string IdempotencyKeyReused = "Api.IdempotencyKeyReused";
        public const string Unexpected = "Api.Unexpected";
        public const string Unreachable = "Api.Unreachable";
    }
}
