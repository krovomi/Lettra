using System.Globalization;
using Lettra.Contracts.Common;
using Lettra.Contracts.Dossiers;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared.Components.Palette;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Cabinet;

/// <summary>
/// Le dossier ouvert et ceux que l'utilisateur peut ouvrir : ce que lisent le sélecteur de la barre du haut, la vue
/// cabinet et la palette. Favoris et récents sont gardés sur l'appareil ; le navigateur y garde aussi le dernier dossier
/// ouvert pour le rouvrir au lancement suivant. Changer de dossier recrée l'écran courant (<see cref="Changed"/>).
/// </summary>
public sealed class CurrentDossier(ICabinetGateway gateway, IDossierSelection selection, UiPreferences preferences)
{
    public const int MaxRecents = 5;
    private const string CurrentKey = "lettra.dossier.courant";
    private const string FavouritesKey = "lettra.dossier.favoris";
    private const string RecentsKey = "lettra.dossier.recents";

    private Task? _loading;
    private List<Guid> _favourites = [];
    private List<Guid> _recents = [];

    public event EventHandler? Changed;

    public IReadOnlyList<DossierSummary> Dossiers { get; private set; } = [];

    public bool IsLoaded { get; private set; }

    public IReadOnlyList<ErrorDetail> Errors { get; private set; } = [];

    /// <summary>L'hôte laisse changer de dossier (navigateur, application reliée au serveur commun, poste isolé câblé).</summary>
    public bool CanSwitch => selection is ISelectableDossierSelection;

    public Guid? Id => selection.CurrentDossierId;

    public string? Name => Dossiers.FirstOrDefault(d => d.Id == Id)?.Name;

    public IReadOnlyList<DossierSummary> Favourites => [.. _favourites.Select(Find).OfType<DossierSummary>()];

    public IReadOnlyList<DossierSummary> Recents => [.. _recents.Where(id => id != Id).Select(Find).OfType<DossierSummary>()];

    public bool IsFavourite(Guid dossierId) => _favourites.Contains(dossierId);

    /// <summary>Identifiants des favoris et des récents, tels que le compte de l'utilisateur les retient.</summary>
    public IReadOnlyList<Guid> FavouriteIds => _favourites;

    public IReadOnlyList<Guid> RecentIds => _recents;

    /// <summary>
    /// Reprend les favoris et récents rangés dans le compte (réglés sur un autre appareil) et les retient aussi sur
    /// celui-ci. Les dossiers devenus inaccessibles sont écartés.
    /// </summary>
    public async Task AdoptAsync(IEnumerable<Guid> favourites, IEnumerable<Guid> recents)
    {
        ArgumentNullException.ThrowIfNull(favourites);
        ArgumentNullException.ThrowIfNull(recents);
        await EnsureLoadedAsync().ConfigureAwait(false);
        List<Guid> adoptedFavourites = [.. favourites.Distinct().Where(id => Find(id) is not null)];
        List<Guid> adoptedRecents = [.. recents.Distinct().Where(id => Find(id) is not null).Take(MaxRecents + 1)];
        if (adoptedFavourites.SequenceEqual(_favourites) && adoptedRecents.SequenceEqual(_recents))
        {
            return;
        }

        _favourites = adoptedFavourites;
        _recents = adoptedRecents;
        await preferences.WriteAsync(FavouritesKey, Join(_favourites)).ConfigureAwait(false);
        await preferences.WriteAsync(RecentsKey, Join(_recents)).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Une seule lecture par session ; <paramref name="reload"/> la refait (après une création de dossier).</summary>
    public Task EnsureLoadedAsync(bool reload = false)
    {
        if (reload || _loading is null || _loading.IsFaulted)
        {
            _loading = LoadAsync();
        }

        return _loading;
    }

    public async Task<bool> SwitchAsync(Guid dossierId, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync().ConfigureAwait(false);
        if (selection is not ISelectableDossierSelection selectable || Find(dossierId) is null)
        {
            return false;
        }

        // Le dossier quitté passe en tête des récents, juste derrière celui que l'on ouvre.
        foreach (Guid recent in new[] { Id, dossierId }.OfType<Guid>().Distinct())
        {
            _recents.Remove(recent);
            _recents.Insert(0, recent);
        }

        if (dossierId != Id)
        {
            await selectable.SelectAsync(dossierId, cancellationToken).ConfigureAwait(false);
            await preferences.WriteAsync(CurrentKey, dossierId.ToString("D", CultureInfo.InvariantCulture)).ConfigureAwait(false);
        }

        if (_recents.Count > MaxRecents + 1)
        {
            _recents.RemoveRange(MaxRecents + 1, _recents.Count - MaxRecents - 1);
        }

        await preferences.WriteAsync(RecentsKey, Join(_recents)).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task ToggleFavouriteAsync(Guid dossierId)
    {
        if (!_favourites.Remove(dossierId))
        {
            _favourites.Add(dossierId);
        }

        await preferences.WriteAsync(FavouritesKey, Join(_favourites)).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Recherche sans accents ni casse sur le nom (mêmes règles que la palette), dans l'ordre alphabétique.</summary>
    public IReadOnlyList<DossierSummary> Search(string? query) =>
        [
            .. Dossiers
                .Select(d => (Dossier: d, Score: PaletteMatcher.Score(d.Name, query ?? string.Empty)))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Dossier.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => x.Dossier),
        ];

    private async Task LoadAsync()
    {
        OperationResult<IReadOnlyList<DossierSummary>> listed = await gateway.ListDossiersAsync(CancellationToken.None).ConfigureAwait(false);
        Errors = listed.IsSuccess ? [] : listed.Errors;
        Dossiers = listed.IsSuccess ? [.. (listed.Value ?? []).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)] : [];
        _favourites = Parse(await preferences.ReadAsync(FavouritesKey).ConfigureAwait(false)).Where(id => Find(id) is not null).ToList();
        _recents = Parse(await preferences.ReadAsync(RecentsKey).ConfigureAwait(false)).Where(id => Find(id) is not null).ToList();
        IsLoaded = true;

        // Navigateur : rouvre le dernier dossier choisi sur cet appareil, s'il est toujours accessible.
        Guid? remembered = Parse(await preferences.ReadAsync(CurrentKey).ConfigureAwait(false)).Cast<Guid?>().FirstOrDefault();
        if (selection is ISelectableDossierSelection selectable && remembered is Guid last && last != Id && Find(last) is not null)
        {
            await selectable.SelectAsync(last, CancellationToken.None).ConfigureAwait(false);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private DossierSummary? Find(Guid id) => Dossiers.FirstOrDefault(d => d.Id == id);

    private static List<Guid> Parse(string? stored) =>
        [.. (stored ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => Guid.TryParse(s, out Guid id) ? id : Guid.Empty).Where(id => id != Guid.Empty).Distinct()];

    private static string Join(IEnumerable<Guid> ids) => string.Join(',', ids.Select(id => id.ToString("D", CultureInfo.InvariantCulture)));
}

/// <summary>Palette (Ctrl+K) : « Ouvrir le dossier … » pour chaque dossier accessible, et la vue cabinet.</summary>
internal sealed class DossierPaletteProvider(CurrentDossier dossiers, NavigationManager navigation, IStringLocalizer<SharedResource> localizer)
    : ICommandPaletteProvider
{
    public async Task<IReadOnlyList<PaletteItem>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        await dossiers.EnsureLoadedAsync().ConfigureAwait(false);
        string group = localizer["Cabinet.Palette.Group"];
        List<PaletteItem> items = [];
        string overview = localizer["Cabinet.Title"];
        if (PaletteMatcher.Score(overview, query) > 0)
        {
            items.Add(new PaletteItem(group, overview, () =>
            {
                navigation.NavigateTo("cabinet");
                return Task.CompletedTask;
            }));
        }

        if (dossiers.CanSwitch && !string.IsNullOrWhiteSpace(query))
        {
            foreach (DossierSummary dossier in dossiers.Search(query).Where(d => d.Id != dossiers.Id).Take(8))
            {
                items.Add(new PaletteItem(
                    group,
                    string.Format(CultureInfo.CurrentCulture, localizer["Cabinet.Palette.Open"], dossier.Name),
                    async () =>
                    {
                        if (await dossiers.SwitchAsync(dossier.Id).ConfigureAwait(false))
                        {
                            navigation.NavigateTo(string.Empty);
                        }
                    }));
            }
        }

        return items;
    }
}
