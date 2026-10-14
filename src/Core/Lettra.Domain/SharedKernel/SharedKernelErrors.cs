using Lettra.Domain.Common;

namespace Lettra.Domain.SharedKernel;

/// <summary>Codes d'erreur des objets-valeurs partagés.</summary>
public static class SharedKernelErrors
{
    public const string SirenInvalidCode = "Siren.Invalid";
    public const string SiretInvalidCode = "Siret.Invalid";
    public const string VatNumberInvalidCode = "VatNumber.Invalid";
    public const string EmailInvalidCode = "Email.Invalid";
    public const string CountryCodeInvalidCode = "CountryCode.Invalid";
    public const string PostalCodeInvalidCode = "Address.PostalCodeInvalid";
    public const string AddressLineRequiredCode = "Address.LineRequired";
    public const string CityRequiredCode = "Address.CityRequired";
    public const string CurrencyMismatchCode = "Money.CurrencyMismatch";
    public const string VatRateUnknownCode = "VatRate.Unknown";

    public static Error SirenInvalid => Error.Validation(SirenInvalidCode);
    public static Error SiretInvalid => Error.Validation(SiretInvalidCode);
    public static Error VatNumberInvalid => Error.Validation(VatNumberInvalidCode);
    public static Error EmailInvalid => Error.Validation(EmailInvalidCode);
    public static Error CountryCodeInvalid => Error.Validation(CountryCodeInvalidCode);
    public static Error PostalCodeInvalid => Error.Validation(PostalCodeInvalidCode);
    public static Error AddressLineRequired => Error.Validation(AddressLineRequiredCode);
    public static Error CityRequired => Error.Validation(CityRequiredCode);
    public static Error VatRateUnknown => Error.Validation(VatRateUnknownCode);
}
