using System.Globalization;
using Lettra.Contracts.Preferences;
using Lettra.UI.Shared.Cabinet;
using Lettra.UI.Shared.Keyboard;
using Lettra.UI.Shared.Layout;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Shared.Preferences;

/// <summary>
/// Relie les réglages déjà retenus sur l'appareil (menu, dossiers favoris et récents, thème, densité, langue) au compte
/// de l'utilisateur : ce qui est réglé sur un appareil revient sur les autres, et ce que l'appareil retenait avant
/// d'avoir un compte y est rangé au premier passage. Ouvre aussi l'écran d'accueil choisi. Démarré par la mise en page.
/// Sans compte (poste isolé, serveur plus ancien), chaque réglage reste sur l'appareil, comme avant.
/// </summary>
public sealed class PreferencesSync
{
    private readonly UserPreferencesState _account;
    private readonly UiPreferences _appearance;
    private readonly NavMenuState _menu;
    private readonly CurrentDossier _dossiers;
    private readonly NavigationManager? _navigation;
    private bool _started;
    private bool _homeOpened;
    private bool _moved;
    private bool _dossiersAdopted;
    private UiTheme _theme;
    private UiDensity _density;

    public PreferencesSync(UserPreferencesState account, UiPreferences appearance, NavMenuState menu, CurrentDossier dossiers, Optional<NavigationManager> navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        _account = account;
        _appearance = appearance;
        _menu = menu;
        _dossiers = dossiers;
        _navigation = navigation.Value;
    }

    /// <summary>Une seule fois par session de l'interface, après la lecture des réglages d'affichage de l'appareil.</summary>
    public async Task StartAsync()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _theme = _appearance.Theme;
        _density = _appearance.Density;
        _account.Changed += (_, _) => _ = FromAccountAsync();
        _menu.Changed += (_, _) => _ = ToAccountAsync();
        _dossiers.Changed += (_, _) => _ = OnDossiersChangedAsync();
        _appearance.Changed += (_, _) => _ = ToAccountAsync();
        await _menu.LoadAsync().ConfigureAwait(false);
        await _account.EnsureLoadedAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Le compte a été lu ou modifié : ses réglages s'appliquent ici. Compte encore vide : ceux de l'appareil y sont
    /// rangés une fois, pour ne rien perdre.
    /// </summary>
    private async Task FromAccountAsync()
    {
        if (!_account.InAccount)
        {
            return;
        }

        if (_account.Current.Version is null)
        {
            if (!_moved)
            {
                _moved = true;
                await ToAccountAsync().ConfigureAwait(false);
            }

            return;
        }

        WorkspacePreferencesDto workspace = _account.Workspace;
        await _menu.AdoptAsync(workspace.NavFavorites, workspace.CollapsedSections).ConfigureAwait(false);
        await AdoptDossiersAsync().ConfigureAwait(false);
        if (Enum.TryParse(workspace.Theme, out UiTheme theme) && Enum.IsDefined(theme))
        {
            _theme = theme;
            await _appearance.SetThemeAsync(theme).ConfigureAwait(false);
        }

        if (Enum.TryParse(workspace.Density, out UiDensity density) && Enum.IsDefined(density))
        {
            _density = density;
            await _appearance.SetDensityAsync(density).ConfigureAwait(false);
        }

        await ScreenLanguage.AdoptAsync(_appearance, _navigation, workspace.Language).ConfigureAwait(false);
        OpenHome(workspace.HomePage);
    }

    /// <summary>Les dossiers sont lus après le compte : les favoris du compte d'abord, les changements ensuite.</summary>
    private async Task OnDossiersChangedAsync()
    {
        if (!_dossiersAdopted && _account.InAccount && _account.Current.Version is not null)
        {
            await AdoptDossiersAsync().ConfigureAwait(false);
            return;
        }

        await ToAccountAsync().ConfigureAwait(false);
    }

    private async Task AdoptDossiersAsync()
    {
        if (!_dossiers.IsLoaded)
        {
            return;
        }

        _dossiersAdopted = true;
        await _dossiers.AdoptAsync(_account.Workspace.FavoriteDossiers, _account.Workspace.RecentDossiers).ConfigureAwait(false);
    }

    /// <summary>
    /// Un réglage a changé sur cet appareil : il est rangé dans le compte s'il diffère de ce que le compte retient. Le
    /// thème et la densité ne partent que si l'utilisateur les a changés ici (la densité par défaut dépend de l'écran).
    /// </summary>
    private async Task ToAccountAsync()
    {
        if (!_account.InAccount)
        {
            return;
        }

        WorkspacePreferencesDto current = _account.Workspace;
        bool first = _account.Current.Version is null;
        bool dossiersKnown = _dossiers.IsLoaded && (first || _dossiersAdopted);
        WorkspacePreferencesDto wanted = current with
        {
            // Un écran que cet appareil ne connaît pas (version plus ancienne) reste en favori pour les autres.
            NavFavorites = [.. _menu.Favorites, .. current.NavFavorites.Where(h => !NavItems.All.Any(i => string.Equals(i.Href, h, StringComparison.Ordinal)))],
            CollapsedSections = _menu.CollapsedSections,
            FavoriteDossiers = dossiersKnown ? [.. _dossiers.FavouriteIds] : current.FavoriteDossiers,
            RecentDossiers = dossiersKnown ? [.. _dossiers.RecentIds] : current.RecentDossiers,
            Theme = _appearance.Theme != _theme || (first && _appearance.Theme != UiTheme.System) ? _appearance.Theme.ToString() : current.Theme,
            Density = _appearance.Density != _density ? _appearance.Density.ToString() : current.Density,
        };

        if (first ? !HasDeviceSettings(wanted) : SameAs(current, wanted))
        {
            return;
        }

        _theme = _appearance.Theme;
        _density = _appearance.Density;
        await _account.UpdateWorkspaceAsync(_ => wanted).ConfigureAwait(false);
    }

    /// <summary>Écran d'accueil choisi : ouvert une fois, à la connexion, si l'utilisateur est encore sur l'accueil.</summary>
    private void OpenHome(string? homePage)
    {
        if (_homeOpened || _navigation is null || string.IsNullOrEmpty(homePage))
        {
            return;
        }

        _homeOpened = true;
        if (_navigation.ToBaseRelativePath(_navigation.Uri).Split('?', '#')[0].Length == 0
            && NavItems.All.Any(i => string.Equals(i.Href, homePage, StringComparison.Ordinal)))
        {
            _navigation.NavigateTo(homePage);
        }
    }

    private static bool SameAs(WorkspacePreferencesDto a, WorkspacePreferencesDto b) =>
        a.Theme == b.Theme
        && a.Density == b.Density
        && a.NavFavorites.SequenceEqual(b.NavFavorites, StringComparer.Ordinal)
        && a.CollapsedSections.SequenceEqual(b.CollapsedSections, StringComparer.Ordinal)
        && a.FavoriteDossiers.SequenceEqual(b.FavoriteDossiers)
        && a.RecentDossiers.SequenceEqual(b.RecentDossiers);

    private static bool HasDeviceSettings(WorkspacePreferencesDto workspace) =>
        workspace.NavFavorites.Count > 0 || workspace.CollapsedSections.Count > 0 || workspace.FavoriteDossiers.Count > 0
        || workspace.RecentDossiers.Count > 0 || workspace.Theme is not null || workspace.Density is not null;
}

/// <summary>
/// Langue de l'écran choisie par l'utilisateur : retenue sur l'appareil (clé <see cref="StorageKey"/>) pour être posée
/// dès le démarrage, et dans son compte. Le changement passe par <see cref="LettraCultures"/>, le point d'entrée unique
/// des formats (docs/dates-et-langue.md). Le navigateur ne charge les textes d'une langue qu'au démarrage : changer de
/// langue recharge donc la page, une fois.
/// </summary>
public static class ScreenLanguage
{
    public const string StorageKey = "lettra.langue";

    /// <summary>Au démarrage de l'hôte, avant le premier rendu : la langue retenue, sinon <paramref name="fallback"/>.</summary>
    public static async Task ApplyStoredAsync(IServiceProvider services, string fallback)
    {
        ArgumentNullException.ThrowIfNull(services);
        string? stored = await new UiPreferences(services.GetRequiredService<Microsoft.JSInterop.IJSRuntime>()).ReadAsync(StorageKey).ConfigureAwait(false);
        LettraCultures.Apply(LettraCultures.Resolve(string.IsNullOrEmpty(stored) ? fallback : stored));
    }

    /// <summary>Choisie dans « Mes préférences » : posée tout de suite, retenue, puis la page est rechargée.</summary>
    public static async Task ChooseAsync(UiPreferences device, NavigationManager? navigation, string? language)
    {
        ArgumentNullException.ThrowIfNull(device);
        await device.WriteAsync(StorageKey, language ?? string.Empty).ConfigureAwait(false);
        LettraCultures.Apply(LettraCultures.Resolve(string.IsNullOrEmpty(language) ? CultureInfo.InstalledUICulture.Name : language));
        navigation?.NavigateTo(navigation.Uri, forceLoad: true);
    }

    /// <summary>
    /// Langue lue dans le compte : si l'appareil retenait une autre langue, elle est adoptée (un rechargement). Une
    /// langue déjà retenue sur l'appareil ne recharge rien, ce qui évite toute boucle.
    /// </summary>
    public static async Task AdoptAsync(UiPreferences device, NavigationManager? navigation, string? language)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (string.IsNullOrEmpty(language))
        {
            return;
        }

        string? stored = await device.ReadAsync(StorageKey).ConfigureAwait(false);
        if (string.Equals(stored, language, StringComparison.Ordinal))
        {
            if (!string.Equals(CultureInfo.CurrentUICulture.Name, LettraCultures.Resolve(language).Name, StringComparison.Ordinal))
            {
                LettraCultures.Apply(LettraCultures.Resolve(language));
            }

            return;
        }

        await ChooseAsync(device, navigation, language).ConfigureAwait(false);
    }
}
