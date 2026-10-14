using System.Globalization;
using Lettra.Domain.Common;

namespace Lettra.Domain.SharedKernel;

/// <summary>
/// Numéro de TVA intracommunautaire. Pour la France, la clé est vérifiée :
/// <c>FR</c> + clé (2 chiffres) + SIREN, avec clé = (12 + 3 × (SIREN mod 97)) mod 97.
/// Pour les autres pays, seul le format général (2 lettres + 2 à 13 caractères) est contrôlé ;
/// la vérification d'existence passe par VIES (adaptateur externe).
/// </summary>
public sealed record VatNumber
{
    private VatNumber(string value) => Value = value;

    public string Value { get; }

    public string CountryPrefix => Value[..2];

    /// <summary>SIREN contenu dans un numéro de TVA français (<c>FR</c> + clé + SIREN), sinon <c>null</c>.</summary>
    public string? FrenchSiren => CountryPrefix == "FR" ? Value[4..] : null;

    public static Result<VatNumber> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return SharedKernelErrors.VatNumberInvalid;
        }

        string value = Luhn.Compact(raw).ToUpperInvariant();
        if (value.Length is < 4 or > 15 || !char.IsAsciiLetterUpper(value[0]) || !char.IsAsciiLetterUpper(value[1]))
        {
            return SharedKernelErrors.VatNumberInvalid;
        }

        if (value.StartsWith("FR", StringComparison.Ordinal) && !IsValidFrench(value))
        {
            return SharedKernelErrors.VatNumberInvalid;
        }

        return value.Skip(2).All(char.IsAsciiLetterOrDigit)
            ? new VatNumber(value)
            : SharedKernelErrors.VatNumberInvalid;
    }

    /// <summary>Numéro de TVA français déduit d'un SIREN.</summary>
    public static VatNumber FromSiren(Siren siren)
    {
        ArgumentNullException.ThrowIfNull(siren);
        return new VatNumber($"FR{FrenchKey(siren.Value):00}{siren.Value}");
    }

    private static bool IsValidFrench(string value)
    {
        if (value.Length != 13 || !Luhn.AllDigits(value.AsSpan(2)))
        {
            return false;
        }

        string siren = value[4..];
        int key = int.Parse(value.AsSpan(2, 2), CultureInfo.InvariantCulture);
        return Luhn.IsValid(siren) && key == FrenchKey(siren);
    }

    private static int FrenchKey(string siren) =>
        (int)((12 + (3 * (long.Parse(siren, CultureInfo.InvariantCulture) % 97))) % 97);

    public override string ToString() => Value;
}
