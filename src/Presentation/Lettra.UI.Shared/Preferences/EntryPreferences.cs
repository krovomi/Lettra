using Lettra.Contracts.Preferences;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared.Services;

namespace Lettra.UI.Shared.Preferences;

/// <summary>
/// Ce que les écrans de saisie lisent des préférences, dans le dossier ouvert : valeurs pré-remplies, listes de choix
/// classées selon l'usage, choix comptés. Les écrans la reçoivent par <c>Optional&lt;EntryPreferences&gt;</c> :
/// absente (hôte sans préférences), la saisie se comporte comme avant.
/// </summary>
public sealed class EntryPreferences(UserPreferencesState preferences, Optional<IDossierSelection> selection)
{
    private IDossierSelection? Selection => selection.Value;

    public Guid? DossierId => Selection?.CurrentDossierId is Guid id && id != Guid.Empty ? id : null;

    public UserPreferencesState Preferences => preferences;

    /// <summary>Valeurs de saisie du dossier ouvert, lues (une fois) avant de les rendre.</summary>
    public async Task<EntryDefaultsDto> DefaultsAsync()
    {
        await preferences.EnsureLoadedAsync().ConfigureAwait(false);
        return preferences.DefaultsFor(DossierId);
    }

    public EntryDefaultsDto Defaults => preferences.DefaultsFor(DossierId);

    public IReadOnlyList<T> Rank<T>(IEnumerable<T> items, Func<T, Guid> id, UsageKindDto kind) =>
        preferences.RankItems(items, id, DossierId, kind);

    public IReadOnlyList<Guid> Ranked(UsageKindDto kind) => preferences.Ranked(DossierId, kind);

    public Task RecordUseAsync(UsageKindDto kind, Guid id) => preferences.RecordUseAsync(DossierId, kind, id);

    /// <summary>« Retenir par défaut » : la valeur saisie devient celle des prochaines pièces du dossier.</summary>
    public async Task<bool> RememberAsync(Func<EntryDefaultsDto, EntryDefaultsDto> change)
    {
        if (DossierId is not Guid dossier)
        {
            return false;
        }

        return (await preferences.UpdateDefaultsAsync(dossier, change).ConfigureAwait(false)).Count == 0;
    }
}
