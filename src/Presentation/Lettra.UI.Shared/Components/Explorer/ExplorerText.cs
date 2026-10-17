using System.Globalization;
using Lettra.UI.Shared.Components.DataEntryGrid;

namespace Lettra.UI.Shared.Components.Explorer;

/// <summary>Mises en forme des pages de consultation : initiales, identifiants groupés, montants en euros.</summary>
public static class ExplorerText
{
    /// <summary>Deux initiales pour la pastille (« Boulangerie Martin » donne « BM »).</summary>
    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "?";
        }

        string[] words = name.Split([' ', '-', '\'', '’', '&'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetterOrDigit(w[0]))
            .ToArray();
        return words.Length switch
        {
            0 => name.Trim()[0].ToString().ToUpperInvariant(),
            1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
            _ => string.Concat(char.ToUpperInvariant(words[0][0]), char.ToUpperInvariant(words[1][0])),
        };
    }

    /// <summary>SIREN groupé par trois : 732 829 320.</summary>
    public static string Siren(string siren) => Group(siren, [3, 3, 3]);

    /// <summary>SIRET : le SIREN groupé puis le NIC, 732 829 320 00074.</summary>
    public static string Siret(string siret) => Group(siret, [3, 3, 3, 5]);

    /// <summary>IBAN par blocs de quatre, comme sur un RIB.</summary>
    public static string Iban(string iban)
    {
        string compact = new([.. iban.Where(c => !char.IsWhiteSpace(c))]);
        return string.Join(' ', compact.Chunk(4).Select(c => new string(c)));
    }

    /// <summary>Montant au centime avec le symbole euro, dans la culture courante (1 234,50 €).</summary>
    public static string Money(decimal value) =>
        GridNumber.FormatAmount(value, CultureInfo.CurrentCulture) + " €";

    private static string Group(string value, int[] sizes)
    {
        string digits = new([.. value.Where(char.IsAsciiDigit)]);
        if (digits.Length != sizes.Sum())
        {
            return value;
        }

        List<string> parts = [];
        int start = 0;
        foreach (int size in sizes)
        {
            parts.Add(digits.Substring(start, size));
            start += size;
        }

        return string.Join(' ', parts);
    }
}
