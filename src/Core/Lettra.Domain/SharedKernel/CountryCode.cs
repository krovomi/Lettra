using Lettra.Domain.Common;

namespace Lettra.Domain.SharedKernel;

/// <summary>Code pays ISO 3166-1 alpha-2 (« FR », « BE »…).</summary>
public sealed record CountryCode
{
    public static readonly CountryCode France = new("FR");

    private CountryCode(string value) => Value = value;

    public string Value { get; }

    public bool IsFrance => Value == France.Value;

    public static Result<CountryCode> Create(string? raw)
    {
        string value = (raw ?? string.Empty).Trim().ToUpperInvariant();
        return value.Length == 2 && value.All(char.IsAsciiLetterUpper)
            ? new CountryCode(value)
            : SharedKernelErrors.CountryCodeInvalid;
    }

    public override string ToString() => Value;
}
