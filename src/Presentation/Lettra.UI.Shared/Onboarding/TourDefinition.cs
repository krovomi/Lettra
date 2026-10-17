namespace Lettra.UI.Shared.Onboarding;

/// <summary>
/// Une étape d'un parcours : l'élément montré et son texte. <see cref="Targets"/> sont des sélecteurs CSS essayés dans
/// l'ordre (d'abord <c>[data-tour=…]</c>, puis les <c>id</c> déjà présents) ; si aucun n'est à l'écran, la bulle
/// s'affiche au centre et le parcours continue. Les textes sont dans les ressources sous
/// <c>Tour.&lt;parcours&gt;.&lt;étape&gt;.Title</c> et <c>.Text</c>.
/// </summary>
public sealed record TourStep(string Id, params string[] Targets);

/// <summary>
/// Un parcours guidé, joué une fois à la première visite de l'écran. Augmenter <see cref="Version"/> quand le
/// parcours change le fait rejouer à ceux qui l'avaient déjà vu.
/// </summary>
/// <param name="Id">Identifiant stable (clés de ressources, état « vu »).</param>
/// <param name="Version">Version du contenu.</param>
/// <param name="Route">Chemin relatif de l'écran : exact (<c>quotes</c>), ou préfixe suivi de <c>/*</c> (<c>quotes/*</c>
/// pour <c>quotes/new</c> et <c>quotes/{id}</c>). <c>null</c> : parcours lancé par l'application, pas par un écran.</param>
/// <param name="Steps">Étapes, dans l'ordre.</param>
public sealed record TourDefinition(string Id, int Version, string? Route, IReadOnlyList<TourStep> Steps)
{
    public string TitleKey(TourStep step) => $"Tour.{Id}.{step.Id}.Title";

    public string TextKey(TourStep step) => $"Tour.{Id}.{step.Id}.Text";

    /// <summary>L'écran affiché est-il celui du parcours ? <paramref name="path"/> est relatif à la base, sans requête.</summary>
    public bool Matches(string path)
    {
        if (Route is null)
        {
            return false;
        }

        string current = TourRoutes.Normalize(path);
        if (Route.EndsWith("/*", StringComparison.Ordinal))
        {
            string prefix = Route[..^1];
            return current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && current.Length > prefix.Length;
        }

        return string.Equals(current, Route, StringComparison.OrdinalIgnoreCase);
    }
}

public static class TourRoutes
{
    /// <summary>Chemin sans requête, sans fragment et sans barre oblique au bord : <c>quotes/new?customer=…</c> → <c>quotes/new</c>.</summary>
    public static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        int cut = path.IndexOfAny(['?', '#']);
        return (cut >= 0 ? path[..cut] : path).Trim('/');
    }
}
