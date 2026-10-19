using System.Globalization;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Shared.Onboarding;

/// <summary>Module JS du tutoriel : élément montré, suivi du défilement, retour du focus.</summary>
public static class TourModule
{
    public const string Path = "./_content/Lettra.UI.Shared/lettra-tour.js";
}

/// <summary>
/// Parcours guidés (vague 5, lane F) : quel parcours est affiché, à quelle étape, et lesquels l'utilisateur a déjà vus.
/// L'état « vu » est retenu sur l'appareil, par utilisateur et par version du parcours (un parcours modifié se rejoue),
/// dans le stockage local du navigateur ou du WebView MAUI. Sans stockage (tests, navigation privée), il vaut pour la
/// session.
/// </summary>
public sealed class GuidedTours(DevicePreferences preferences, IServiceProvider services)
{
    private const string KeyPrefix = "lettra.tours.";

    private Dictionary<string, int>? _seen;
    private string? _storageKey;

    public event EventHandler? Changed;

    public TourDefinition? Active { get; private set; }

    public int StepIndex { get; private set; }

    public TourStep? Step => Active is { } tour && StepIndex < tour.Steps.Count ? tour.Steps[StepIndex] : null;

    public bool IsFirst => StepIndex == 0;

    public bool IsLast => Active is { } tour && StepIndex == tour.Steps.Count - 1;

    /// <summary>Le parcours a-t-il déjà été vu dans sa version actuelle ?</summary>
    public async Task<bool> HasSeenAsync(TourDefinition tour)
    {
        ArgumentNullException.ThrowIfNull(tour);
        Dictionary<string, int> seen = await LoadAsync().ConfigureAwait(false);
        return seen.TryGetValue(tour.Id, out int version) && version >= tour.Version;
    }

    /// <summary>Lance le parcours s'il n'a pas encore été vu (première visite de l'écran).</summary>
    public async Task<bool> StartIfNewAsync(TourDefinition tour)
    {
        if (Active is not null || await HasSeenAsync(tour).ConfigureAwait(false))
        {
            return false;
        }

        Start(tour);
        return true;
    }

    /// <summary>Lance le parcours depuis le début (bouton « ? », palette).</summary>
    public void Start(TourDefinition tour)
    {
        ArgumentNullException.ThrowIfNull(tour);
        if (tour.Steps.Count == 0)
        {
            return;
        }

        Active = tour;
        StepIndex = 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Next()
    {
        if (Active is not null && !IsLast)
        {
            StepIndex++;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Previous()
    {
        if (Active is not null && !IsFirst)
        {
            StepIndex--;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Termine ou passe le parcours : dans les deux cas il ne se relance plus tout seul.</summary>
    public async Task CloseAsync()
    {
        if (Active is not { } tour)
        {
            return;
        }

        Active = null;
        StepIndex = 0;
        Changed?.Invoke(this, EventArgs.Empty);
        Dictionary<string, int> seen = await LoadAsync().ConfigureAwait(false);
        seen[tour.Id] = tour.Version;
        await SaveAsync(seen).ConfigureAwait(false);
    }

    /// <summary>« Réinitialiser les tutoriels » : chaque écran rejouera le sien à la prochaine visite.</summary>
    public async Task ResetAsync()
    {
        Dictionary<string, int> seen = await LoadAsync().ConfigureAwait(false);
        seen.Clear();
        await SaveAsync(seen).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Format compact « home:1;quotes:2 », sans sérialiseur (WASM découpé).</summary>
    public static Dictionary<string, int> Parse(string? stored)
    {
        Dictionary<string, int> seen = new(StringComparer.Ordinal);
        foreach (string entry in (stored ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = entry.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int version))
            {
                seen[parts[0]] = version;
            }
        }

        return seen;
    }

    public static string Format(IReadOnlyDictionary<string, int> seen) =>
        string.Join(';', seen.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}:{p.Value.ToString(CultureInfo.InvariantCulture)}"));

    /// <summary>
    /// Clé de stockage propre à l'utilisateur : empreinte de son nom et du mode de session, pour que deux personnes sur
    /// le même poste aient chacune leurs tutoriels, sans écrire leur nom en clair.
    /// </summary>
    public static string StorageKey(string? user) => KeyPrefix + UserStorageKey.Hash(user);

    private async Task<Dictionary<string, int>> LoadAsync()
    {
        if (_seen is not null)
        {
            return _seen;
        }

        _storageKey ??= StorageKey(await CurrentUserAsync().ConfigureAwait(false));
        string? stored = await preferences.ReadAsync(_storageKey).ConfigureAwait(false);
        return _seen ??= Parse(stored);
    }

    private Task SaveAsync(Dictionary<string, int> seen)
    {
        string value = Format(seen);
        return preferences.WriteAsync(_storageKey ?? StorageKey(null), value);
    }

    private async Task<string?> CurrentUserAsync()
    {
        if (services.GetService<ISessionGateway>() is not { } session)
        {
            return null;
        }

        try
        {
            var result = await session.GetAsync(CancellationToken.None).ConfigureAwait(false);
            return UserStorageKey.UserOf(result.Value);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
