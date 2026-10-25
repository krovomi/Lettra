namespace Lettra.Infrastructure.Persistence.Search;

/// <summary>
/// La règle unique de recherche des listes, traduite à l'identique par les trois providers : un nom ou un libellé se
/// cherche sur sa forme sans accents (<see cref="SearchKey"/>, calculée en C#), un code (numéro, référence, adresse
/// e-mail) en majuscules, par <c>UPPER(...)</c> SQL.
/// </summary>
public sealed record ListSearchTerm(string Text, string Code)
{
    /// <summary>Le terme saisi, ou <c>null</c> s'il est vide : la liste n'est alors pas filtrée.</summary>
    public static ListSearchTerm? From(string? search)
    {
        string? trimmed = search?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : new ListSearchTerm(SearchText.Normalize(trimmed), trimmed.ToUpperInvariant());
    }
}
