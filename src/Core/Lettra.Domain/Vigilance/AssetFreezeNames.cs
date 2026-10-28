using Lettra.Domain.Parties;

namespace Lettra.Domain.Vigilance;

/// <summary>
/// Criblage local d'un nom sur le registre national des gels des avoirs : sans accents, casse, ponctuation, forme juridique
/// ni ordre des mots, avec une faute de frappe tolérée. Un nom abrégé ne compte que s'il garde au moins deux mots, pour ne
/// pas signaler tous les « Martin ». Le résultat est une correspondance possible, jamais une certitude : un homonyme se
/// lève en comparant la date de naissance ou l'adresse.
/// </summary>
public static class AssetFreezeNames
{
    public static bool Matches(string? candidate, string? listed)
    {
        IReadOnlyList<string> left = PayeeNames.Words(candidate);
        IReadOnlyList<string> right = PayeeNames.Words(listed);
        return PayeeNames.Compare(candidate, listed) switch
        {
            PayeeMatch.Match => true,
            PayeeMatch.CloseMatch => Math.Min(left.Count, right.Count) >= 2,
            _ => false,
        };
    }

    /// <summary>Le premier nom inscrit qui ressemble à l'un des noms donnés (le tiers, ses bénéficiaires, le titulaire du compte).</summary>
    public static string? FirstMatch(IEnumerable<string?> candidates, IEnumerable<string> listed)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(listed);
        List<string> names = [.. listed];
        return candidates.Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => names.FirstOrDefault(n => Matches(c, n)))
            .FirstOrDefault(n => n is not null);
    }
}
