namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : codes communs (validation, noyau partagé, API).</summary>
public static partial class ErrorCodes
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

    public static class Api
    {
        public const string IdempotencyKeyReused = "Api.IdempotencyKeyReused";
        public const string Unexpected = "Api.Unexpected";
        public const string Unreachable = "Api.Unreachable";
    }
}
