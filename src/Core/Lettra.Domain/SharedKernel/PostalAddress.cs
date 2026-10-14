using Lettra.Domain.Common;

namespace Lettra.Domain.SharedKernel;

/// <summary>Adresse postale. En France, le code postal compte 5 chiffres.</summary>
public sealed record PostalAddress
{
    public const int MaxLineLength = 200;

    private PostalAddress(string line1, string? line2, string postalCode, string city, CountryCode country)
    {
        Line1 = line1;
        Line2 = line2;
        PostalCode = postalCode;
        City = city;
        Country = country;
    }

    public string Line1 { get; }

    public string? Line2 { get; }

    public string PostalCode { get; }

    public string City { get; }

    public CountryCode Country { get; }

    public static Result<PostalAddress> Create(string? line1, string? line2, string? postalCode, string? city, string? countryCode)
    {
        List<Error> errors = [];
        string l1 = (line1 ?? string.Empty).Trim();
        string? l2 = string.IsNullOrWhiteSpace(line2) ? null : line2.Trim();
        string zip = (postalCode ?? string.Empty).Trim();
        string town = (city ?? string.Empty).Trim();

        if (l1.Length is 0 or > MaxLineLength)
        {
            errors.Add(SharedKernelErrors.AddressLineRequired.For(nameof(Line1)));
        }

        if (town.Length is 0 or > MaxLineLength)
        {
            errors.Add(SharedKernelErrors.CityRequired.For(nameof(City)));
        }

        Result<CountryCode> country = CountryCode.Create(string.IsNullOrWhiteSpace(countryCode) ? CountryCode.France.Value : countryCode);
        if (country.IsFailure)
        {
            errors.Add(SharedKernelErrors.CountryCodeInvalid.For(nameof(Country)));
        }
        else if (!IsValidPostalCode(zip, country.Value))
        {
            errors.Add(SharedKernelErrors.PostalCodeInvalid.For(nameof(PostalCode)));
        }

        return errors.Count > 0
            ? Result<PostalAddress>.Failure(errors)
            : new PostalAddress(l1, l2, zip, town, country.Value);
    }

    private static bool IsValidPostalCode(string zip, CountryCode country) =>
        country.IsFrance
            ? zip.Length == 5 && Luhn.AllDigits(zip)
            : zip.Length is > 0 and <= 16;
}
