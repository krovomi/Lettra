namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : module Tiers (clients, fournisseurs, annuaires).</summary>
public static partial class ErrorCodes
{
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

    /// <summary>Annuaires publics consultés depuis Lettra (SIRENE, VIES).</summary>
    public static class Directory
    {
        public const string QueryTooShort = "Directory.QueryTooShort";
        public const string Unavailable = "Directory.Unavailable";
        public const string VatCountryNotSupported = "Directory.VatCountryNotSupported";
    }
}
