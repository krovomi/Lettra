using System.Globalization;
using System.Text;

namespace Lettra.Domain.Parties;

/// <summary>
/// Réponse d'une vérification du bénéficiaire (« Verification of Payee », virements SEPA depuis octobre 2025) : le nom
/// donné correspond au titulaire du compte, y correspond presque, n'y correspond pas, ou la banque ne peut pas le dire.
/// </summary>
public enum PayeeMatch
{
    Match = 1,
    CloseMatch = 2,
    NoMatch = 3,
    NotPossible = 4,
}

/// <summary>
/// Comparaison locale de deux noms de bénéficiaire, sur le modèle des réponses des banques : sans accents ni casse,
/// sans ponctuation, sans forme juridique ni petits mots, dans n'importe quel ordre. Une faute de frappe ou un nom
/// abrégé donne « correspond presque » ; deux noms sans rapport, « ne correspond pas ».
/// </summary>
public static class PayeeNames
{
    /// <summary>Au-delà, deux noms proches ne diffèrent que d'une faute de frappe ou deux.</summary>
    public const double CloseSimilarity = 0.85;

    private static readonly HashSet<string> Ignored =
    [
        // Formes juridiques françaises et étrangères courantes.
        "SA", "SAS", "SASU", "SARL", "EURL", "SNC", "SCI", "SCP", "SCOP", "SCM", "SELARL", "SELAS", "SEL", "EI", "EIRL", "GIE",
        "GMBH", "AG", "KG", "LTD", "LIMITED", "LLC", "INC", "BV", "NV", "SPA", "SRL", "SL",
        // Mots qui ne distinguent personne.
        "STE", "SOCIETE", "ETS", "ETABLISSEMENTS", "LE", "LA", "LES", "L", "DE", "DU", "DES", "D", "ET", "EN", "THE", "AND", "OF",
    ];

    /// <summary>Les mots qui distinguent un nom, triés : « Sté Assurance de la Vallée SA » donne ASSURANCE VALLEE.</summary>
    public static IReadOnlyList<string> Words(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        StringBuilder text = new(name.Length);
        foreach (char c in name.Replace("&", " ET ", StringComparison.Ordinal).Normalize(NormalizationForm.FormD))
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            text.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : ' ');
        }

        return [.. text.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !Ignored.Contains(w)).Order(StringComparer.Ordinal)];
    }

    /// <summary>Compare le nom attendu au nom du titulaire : jamais <see cref="PayeeMatch.NotPossible"/>, réservé aux banques.</summary>
    public static PayeeMatch Compare(string? expected, string? holder)
    {
        IReadOnlyList<string> left = Words(expected);
        IReadOnlyList<string> right = Words(holder);
        if (left.Count == 0 || right.Count == 0)
        {
            return PayeeMatch.NoMatch;
        }

        if (left.SequenceEqual(right))
        {
            return PayeeMatch.Match;
        }

        // Un nom abrégé (« Durand » pour « Papeterie Durand ») : tous les mots de l'un se retrouvent dans l'autre.
        (IReadOnlyList<string> shorter, IReadOnlyList<string> longer) = left.Count <= right.Count ? (left, right) : (right, left);
        if (shorter.All(longer.Contains) && string.Concat(shorter).Length >= 3)
        {
            return PayeeMatch.CloseMatch;
        }

        return Similarity(string.Join(' ', left), string.Join(' ', right)) >= CloseSimilarity ? PayeeMatch.CloseMatch : PayeeMatch.NoMatch;
    }

    /// <summary>1 pour deux textes identiques, 0 pour deux textes sans rien de commun (distance d'édition rapportée à la longueur).</summary>
    public static double Similarity(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        int longest = Math.Max(left.Length, right.Length);
        return longest == 0 ? 1d : 1d - ((double)Distance(left, right) / longest);
    }

    private static int Distance(string left, string right)
    {
        int[] previous = [.. Enumerable.Range(0, right.Length + 1)];
        int[] current = new int[right.Length + 1];
        for (int i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= right.Length; j++)
            {
                int substitution = previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
