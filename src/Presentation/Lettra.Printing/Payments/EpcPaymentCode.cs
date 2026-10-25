using System.Globalization;
using System.Text;

namespace Lettra.Printing.Payments;

/// <summary>
/// Contenu du QR code de virement du Conseil européen des paiements (EPC069-12, version 002) : l'application bancaire
/// qui le lit ouvre un virement SEPA déjà rempli. Le libellé du virement porte le numéro de la facture, que le
/// rapprochement bancaire reconnaît ensuite tout seul (règle « numéro de facture »).
/// </summary>
public static class EpcPaymentCode
{
    /// <summary>Plafond du standard (999 999 999,99 €).</summary>
    public const decimal MaxAmount = 999_999_999.99m;

    private const int MaxNameLength = 70;
    private const int MaxRemittanceLength = 140;

    /// <summary>
    /// Texte à encoder, ou <c>null</c> quand le virement ne peut pas être prérempli : IBAN absent ou invalide, montant
    /// nul, négatif ou hors plafond, bénéficiaire sans nom.
    /// </summary>
    public static string? Payload(string beneficiary, string? iban, string? bic, decimal amount, string remittance)
    {
        ArgumentNullException.ThrowIfNull(beneficiary);
        ArgumentNullException.ThrowIfNull(remittance);
        string? account = NormalizedIban(iban);
        decimal rounded = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        string name = Truncate(OneLine(beneficiary), MaxNameLength);
        if (account is null || rounded < 0.01m || rounded > MaxAmount || name.Length == 0)
        {
            return null;
        }

        StringBuilder text = new();
        text.Append("BCD\n002\n1\nSCT\n")
            .Append(NormalizedBic(bic)).Append('\n')
            .Append(name).Append('\n')
            .Append(account).Append('\n')
            .Append("EUR").Append(rounded.ToString("0.00", CultureInfo.InvariantCulture)).Append('\n')
            .Append('\n')
            .Append('\n')
            .Append(Truncate(OneLine(remittance), MaxRemittanceLength));
        return text.ToString();
    }

    /// <summary>IBAN sans espaces, en majuscules, si sa clé (modulo 97, ISO 13616) est juste ; <c>null</c> sinon.</summary>
    public static string? NormalizedIban(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban))
        {
            return null;
        }

        string value = string.Concat(iban.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        if (value.Length is < 15 or > 34 || !char.IsAsciiLetter(value[0]) || !char.IsAsciiLetter(value[1])
            || !char.IsAsciiDigit(value[2]) || !char.IsAsciiDigit(value[3]) || !value.All(char.IsAsciiLetterOrDigit))
        {
            return null;
        }

        int remainder = 0;
        foreach (char c in value[4..] + value[..4])
        {
            int digit = char.IsAsciiDigit(c) ? c - '0' : c - 'A' + 10;
            remainder = ((remainder * (digit >= 10 ? 100 : 10)) + digit) % 97;
        }

        return remainder == 1 ? value : null;
    }

    private static string NormalizedBic(string? bic)
    {
        string value = string.Concat((bic ?? string.Empty).Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        return value.Length is 8 or 11 && value.All(char.IsAsciiLetterOrDigit) ? value : string.Empty;
    }

    private static string OneLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Trim();

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length].TrimEnd();
}
