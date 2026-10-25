using System.Globalization;

namespace Lettra.UI.Shared.Demo;

/// <summary>
/// Valeurs propres à une lecture de la démo : la démo peut être rejouée dans le même dossier sans buter sur une
/// référence d'article ou un SIREN déjà pris. Jetons remplacés dans les textes tapés :
/// <list type="bullet">
/// <item><c>{run}</c> : code court de la lecture (4 caractères, chiffres et majuscules) ;</item>
/// <item><c>{siren}</c> : SIREN fictif valide (clé de Luhn), commençant par 92, comme les SIREN fictifs de la démo.</item>
/// </list>
/// </summary>
public sealed class DemoValues
{
    /// <summary>Valeur à lire dans les ressources (« res:Demo.Value.Customer ») : elle suit la langue de l'écran.</summary>
    public const string ResourcePrefix = "res:";

    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public DemoValues(long seed)
    {
        long value = Math.Abs(seed % 1_679_616);   // 36^4 : quatre caractères
        char[] code = new char[4];
        for (int i = code.Length - 1; i >= 0; i--)
        {
            code[i] = Alphabet[(int)(value % 36)];
            value /= 36;
        }

        Run = new string(code);
        Siren = WithLuhnKey("92" + (Math.Abs(seed) % 1_000_000).ToString("D6", CultureInfo.InvariantCulture));
    }

    public string Run { get; }

    public string Siren { get; }

    /// <summary>Une lecture : code tiré de l'horloge (millisecondes), différent à chaque lancement.</summary>
    public static DemoValues For(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return new DemoValues(clock.GetUtcNow().ToUnixTimeMilliseconds());
    }

    public string Apply(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("{run}", Run, StringComparison.Ordinal).Replace("{siren}", Siren, StringComparison.Ordinal);
    }

    /// <summary>Ajoute la clé de Luhn (dernier chiffre d'un SIREN) aux huit premiers chiffres.</summary>
    public static string WithLuhnKey(string digits)
    {
        ArgumentNullException.ThrowIfNull(digits);
        int sum = 0;
        for (int i = 0; i < digits.Length; i++)
        {
            int digit = digits[digits.Length - 1 - i] - '0';
            if (i % 2 == 0)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
        }

        return digits + ((10 - (sum % 10)) % 10).ToString(CultureInfo.InvariantCulture);
    }
}
