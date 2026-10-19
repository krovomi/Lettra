using Lettra.UI.Abstractions;
using Lettra.UI.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Shared.Layout;

/// <summary>
/// Le menu tel que l'utilisateur l'a réglé : sections repliées et écrans épinglés en favoris. Partagé par la barre
/// latérale et la feuille « Plus » du téléphone, retenu sur l'appareil et par utilisateur (deux personnes sur le même
/// poste ont chacune leur menu). Sans stockage (tests, navigation privée), le réglage vaut pour la session.
/// </summary>
public sealed class NavMenuState
{
    private const string SectionsPrefix = "lettra.nav.sections.";
    private const string FavoritesPrefix = "lettra.nav.favorites.";

    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private readonly List<string> _favorites = [];
    private string? _user;
    private bool _loaded;
    private readonly UiPreferences _preferences;
    private readonly SessionState? _session;

    /// <summary>Sans session (hôte de test, écran isolé), le réglage est celui de l'appareil.</summary>
    public NavMenuState(UiPreferences preferences, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _preferences = preferences;
        _session = services.GetService<ISessionGateway>() is null ? null : services.GetService<SessionState>();
        // Un autre utilisateur ouvre sa session sur le poste : son propre menu revient.
        _session?.Changed += (_, _) => _ = LoadAsync();
    }

    public event EventHandler? Changed;

    /// <summary>Écrans épinglés, dans l'ordre où l'utilisateur les a ajoutés (liens relatifs de <see cref="NavItems"/>).</summary>
    public IReadOnlyList<string> Favorites => _favorites;

    public bool IsCollapsed(string groupKey) => _collapsed.Contains(groupKey);

    public bool IsFavorite(NavItem item) => _favorites.Contains(item.Href, StringComparer.Ordinal);

    /// <summary>Les entrées épinglées parmi <paramref name="items"/>, dans l'ordre des favoris.</summary>
    public IReadOnlyList<NavItem> FavoriteItems(IEnumerable<NavItem> items)
    {
        Dictionary<string, NavItem> byHref = items.ToDictionary(i => i.Href, StringComparer.Ordinal);
        return [.. _favorites.Where(byHref.ContainsKey).Select(href => byHref[href])];
    }

    /// <summary>Lit le réglage de l'utilisateur connecté ; relit si un autre utilisateur a ouvert sa session.</summary>
    public async Task LoadAsync()
    {
        string? user = UserStorageKey.UserOf(_session?.Current);
        if (_loaded && user == _user)
        {
            return;
        }

        _loaded = true;
        _user = user;
        string? sections = await _preferences.ReadAsync(Key(SectionsPrefix)).ConfigureAwait(false);
        string? favorites = await _preferences.ReadAsync(Key(FavoritesPrefix)).ConfigureAwait(false);
        if (user != _user)
        {
            return;   // un autre utilisateur est arrivé pendant la lecture : sa propre lecture l'emporte
        }

        _collapsed.Clear();
        _collapsed.UnionWith(Split(sections));
        _favorites.Clear();
        _favorites.AddRange(Split(favorites).Where(href => NavItems.All.Any(i => i.Href == href)).Distinct(StringComparer.Ordinal));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sections repliées, triées : ce que le compte de l'utilisateur retient.</summary>
    public IReadOnlyList<string> CollapsedSections => [.. _collapsed.Order(StringComparer.Ordinal)];

    /// <summary>
    /// Reprend le menu rangé dans le compte de l'utilisateur (réglé sur un autre appareil) et le retient aussi sur
    /// celui-ci. Les écrans qui n'existent plus sont écartés.
    /// </summary>
    public async Task AdoptAsync(IEnumerable<string> favorites, IEnumerable<string> collapsedSections)
    {
        ArgumentNullException.ThrowIfNull(favorites);
        ArgumentNullException.ThrowIfNull(collapsedSections);
        string[] adoptedFavorites = [.. favorites.Where(href => NavItems.All.Any(i => i.Href == href)).Distinct(StringComparer.Ordinal)];
        string[] adoptedSections = [.. collapsedSections.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        if (adoptedFavorites.SequenceEqual(_favorites, StringComparer.Ordinal) && adoptedSections.SequenceEqual(CollapsedSections, StringComparer.Ordinal))
        {
            return;
        }

        _favorites.Clear();
        _favorites.AddRange(adoptedFavorites);
        _collapsed.Clear();
        _collapsed.UnionWith(adoptedSections);
        Changed?.Invoke(this, EventArgs.Empty);
        await _preferences.WriteAsync(Key(FavoritesPrefix), string.Join(',', _favorites)).ConfigureAwait(false);
        await _preferences.WriteAsync(Key(SectionsPrefix), string.Join(',', adoptedSections)).ConfigureAwait(false);
    }

    public Task ToggleSectionAsync(string groupKey)
    {
        if (!_collapsed.Remove(groupKey))
        {
            _collapsed.Add(groupKey);
        }

        return SaveSectionsAsync();
    }

    public Task ToggleFavoriteAsync(NavItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!_favorites.Remove(item.Href))
        {
            _favorites.Add(item.Href);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return _preferences.WriteAsync(Key(FavoritesPrefix), string.Join(',', _favorites));
    }

    private Task SaveSectionsAsync()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        return _preferences.WriteAsync(Key(SectionsPrefix), string.Join(',', _collapsed.Order(StringComparer.Ordinal)));
    }

    private string Key(string prefix) => prefix + UserStorageKey.Hash(_user);

    private static string[] Split(string? stored) =>
        string.IsNullOrEmpty(stored) ? [] : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
