using System.Globalization;
using System.Text;

namespace Lettra.DemoData;

/// <summary>
/// Identifiants français fictifs mais valides (clé de Luhn, clé TVA, clé RIB, IBAN) : les écrans et la conformité les
/// acceptent comme de vrais numéros. Les SIREN commencent par 9 et suivent une plage réservée à la démonstration :
/// aucun ne désigne une entreprise réelle connue de Lettra.
/// </summary>
internal static class FrenchIdentifiers
{
    /// <summary>Un SIREN de la plage de démonstration (9 chiffres, clé de Luhn), à partir d'un numéro d'ordre.</summary>
    public static string Siren(int ordinal)
    {
        string body = string.Create(CultureInfo.InvariantCulture, $"9{ordinal % 10_000_000:0000000}");
        return body + LuhnCheckDigit(body);
    }

    /// <summary>Le SIRET de l'établissement <paramref name="establishment"/> (NIC sur 4 chiffres + clé de Luhn sur 14).</summary>
    public static string Siret(string siren, int establishment)
    {
        string body = siren + establishment.ToString("0000", CultureInfo.InvariantCulture);
        return body + LuhnCheckDigit(body);
    }

    /// <summary>Numéro de TVA intracommunautaire français : FR + clé (12 + 3 × (SIREN mod 97)) mod 97 + SIREN.</summary>
    public static string VatNumber(string siren)
    {
        long key = (12 + (3 * (long.Parse(siren, CultureInfo.InvariantCulture) % 97))) % 97;
        return string.Create(CultureInfo.InvariantCulture, $"FR{key:00}{siren}");
    }

    /// <summary>
    /// IBAN français fictif (banque 99xxx, inconnue) : clé RIB puis clé IBAN calculées, donc accepté par le contrôle
    /// modulo 97 du domaine.
    /// </summary>
    public static string Iban(int bank, int branch, long account)
    {
        string bankCode = (99_000 + (bank % 1_000)).ToString("00000", CultureInfo.InvariantCulture);
        string branchCode = (branch % 100_000).ToString("00000", CultureInfo.InvariantCulture);
        string accountCode = (account % 100_000_000_000L).ToString("00000000000", CultureInfo.InvariantCulture);
        string bban = bankCode + branchCode + accountCode;
        int ribKey = (int)(97 - (((89 * long.Parse(bankCode, CultureInfo.InvariantCulture))
            + (15 * long.Parse(branchCode, CultureInfo.InvariantCulture))
            + (3 * long.Parse(accountCode, CultureInfo.InvariantCulture))) % 97));
        bban += ribKey.ToString("00", CultureInfo.InvariantCulture);
        int check = 98 - Mod97(bban + "FR00");
        return string.Create(CultureInfo.InvariantCulture, $"FR{check:00}{bban}");
    }

    private static char LuhnCheckDigit(string body)
    {
        int sum = 0;
        bool doubleIt = true;
        for (int i = body.Length - 1; i >= 0; i--)
        {
            int digit = body[i] - '0';
            if (doubleIt)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleIt = !doubleIt;
        }

        return (char)('0' + ((10 - (sum % 10)) % 10));
    }

    private static int Mod97(string value)
    {
        StringBuilder digits = new(value.Length * 2);
        foreach (char c in value)
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
}
