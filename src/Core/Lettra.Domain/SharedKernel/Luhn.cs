namespace Lettra.Domain.SharedKernel;

/// <summary>Clé de Luhn, utilisée par l'INSEE pour le SIREN et le SIRET.</summary>
internal static class Luhn
{
    public static bool IsValid(ReadOnlySpan<char> digits)
    {
        int sum = 0;
        bool doubleIt = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int digit = digits[i] - '0';
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

        return sum % 10 == 0;
    }

    public static bool AllDigits(ReadOnlySpan<char> value)
    {
        foreach (char c in value)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Retire espaces et espaces insécables saisis pour la lisibilité (« 732 829 320 »).</summary>
    public static string Compact(string value) =>
        string.Concat(value.Where(c => !char.IsWhiteSpace(c) && c != ' '));
}
