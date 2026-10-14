using System.Globalization;
using System.Text;
using Lettra.Domain.Common;

namespace Lettra.Domain.SharedKernel;

/// <summary>
/// IBAN (ISO 13616) : 2 lettres de pays, 2 chiffres de clé, puis 11 à 30 caractères alphanumériques.
/// La clé est vérifiée (modulo 97 = 1). La longueur propre à chaque pays n'est contrôlée que pour la France (27).
/// </summary>
public sealed record Iban
{
    private const int FrenchLength = 27;

    private Iban(string value) => Value = value;

    /// <summary>Forme compacte, en majuscules, sans espaces.</summary>
    public string Value { get; }

    public static Result<Iban> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return SharedKernelErrors.IbanInvalid;
        }

        string value = Luhn.Compact(raw).ToUpperInvariant();
        bool wellFormed = value.Length is >= 15 and <= 34
            && char.IsAsciiLetterUpper(value[0]) && char.IsAsciiLetterUpper(value[1])
            && char.IsAsciiDigit(value[2]) && char.IsAsciiDigit(value[3])
            && value.All(char.IsAsciiLetterOrDigit)
            && (!value.StartsWith("FR", StringComparison.Ordinal) || value.Length == FrenchLength);

        return wellFormed && Mod97(value) == 1 ? new Iban(value) : SharedKernelErrors.IbanInvalid;
    }

    /// <summary>Clé ISO 7064 : les 4 premiers caractères passent à la fin, les lettres valent 10 à 35.</summary>
    private static int Mod97(string value)
    {
        StringBuilder digits = new(value.Length * 2);
        foreach (char c in value[4..] + value[..4])
        {
            digits.Append(char.IsAsciiDigit(c) ? (c - '0').ToString(CultureInfo.InvariantCulture) : (c - 'A' + 10).ToString(CultureInfo.InvariantCulture));
        }

        int remainder = 0;
        foreach (char d in digits.ToString())
        {
            remainder = ((remainder * 10) + (d - '0')) % 97;
        }

        return remainder;
    }

    /// <summary>Affichage par groupes de 4 (FR76 3000 6000 …).</summary>
    public string Formatted() => string.Join(' ', Enumerable.Range(0, (Value.Length + 3) / 4).Select(i => Value.Substring(i * 4, Math.Min(4, Value.Length - (i * 4)))));

    public override string ToString() => Value;
}
