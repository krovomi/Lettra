using System.Text.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Preferences;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Shared.Preferences;

/// <summary>
/// Les préférences de l'utilisateur connecté, telles que les écrans les lisent : espace de travail (thème, densité,
/// accueil, menu, dossiers), valeurs de saisie par dossier et choix les plus fréquents. Rangées dans son compte quand
/// le serveur le permet (elles le suivent alors sur tous ses appareils), sinon sur l'appareil, par utilisateur (poste
/// isolé, serveur plus ancien, serveur injoignable). Une copie reste toujours sur l'appareil, pour démarrer sans attendre.
/// </summary>
public sealed class UserPreferencesState : IDisposable
{
    public const int MaxChoicesPerKind = 30;
    public const int MaxDocumentsPerKind = 8;
    private const string KeyPrefix = "lettra.preferences.";

    private static readonly UserPreferencesResponse Nothing = new(new WorkspacePreferencesDto(), [], [], null, null);

    private readonly IUserPreferencesGateway? _gateway;
    private readonly UiPreferences _device;
    private readonly SessionState? _session;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _writing = new(1, 1);
    private string? _user;
    private Task? _loading;

    public UserPreferencesState(UiPreferences device, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _device = device;
        _gateway = services.GetService<IUserPreferencesGateway>();
        _session = services.GetService<ISessionGateway>() is null ? null : services.GetService<SessionState>();
        _clock = services.GetService<TimeProvider>() ?? TimeProvider.System;
        if (_session is not null)
        {
            // Un autre utilisateur ouvre sa session sur le poste : ses propres préférences reviennent.
            _session.Changed += (_, _) => _ = EnsureLoadedAsync();
        }
    }

    /// <summary>Après chaque lecture ou changement (écran à rafraîchir, menu, thème).</summary>
    public event EventHandler? Changed;

    public UserPreferencesResponse Current { get; private set; } = Nothing;

    public WorkspacePreferencesDto Workspace => Current.Workspace;

    /// <summary>Vrai : les préférences sont rangées dans le compte (elles suivent l'utilisateur) ; faux : sur cet appareil.</summary>
    public bool InAccount { get; private set; }

    public bool IsLoaded { get; private set; }

    /// <summary>Lit les préférences de l'utilisateur connecté ; relit si un autre utilisateur a ouvert sa session.</summary>
    public Task EnsureLoadedAsync()
    {
        string? user = UserStorageKey.UserOf(_session?.Current);
        if (_loading is { IsFaulted: false } loading && user == _user)
        {
            return loading;
        }

        _user = user;
        return _loading = LoadAsync(user);
    }

    public void Dispose() => _writing.Dispose();

    public EntryDefaultsDto DefaultsFor(Guid? dossierId) =>
        Current.EntryDefaults.FirstOrDefault(d => d.DossierId == dossierId) ?? new EntryDefaultsDto { DossierId = dossierId ?? Guid.Empty };

    /// <summary>
    /// Les identifiants choisis le plus souvent dans un dossier, du plus utile au moins utile : plus utilisé d'abord, puis
    /// le plus récent. Les pièces (factures, devis) : les dernières ouvertes d'abord. Vide si le classement est coupé.
    /// </summary>
    public IReadOnlyList<Guid> Ranked(Guid? dossierId, UsageKindDto kind)
    {
        if (dossierId is null || (!Workspace.RankByUsage && !IsDocument(kind)))
        {
            return [];
        }

        IEnumerable<UsageEntryDto> entries = (Current.Usage.FirstOrDefault(u => u.DossierId == dossierId)?.Entries ?? []).Where(e => e.Kind == kind);
        return [.. Order(entries, kind).Select(e => e.Id)];
    }

    /// <summary>
    /// Trie <paramref name="items"/> : d'abord ceux choisis le plus souvent (dans l'ordre de <see cref="Ranked"/>), puis
    /// les autres dans leur ordre d'origine. Sans choix compté, la liste est rendue telle quelle.
    /// </summary>
    public IReadOnlyList<T> RankItems<T>(IEnumerable<T> items, Func<T, Guid> id, Guid? dossierId, UsageKindDto kind)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(id);
        IReadOnlyList<Guid> ranked = Ranked(dossierId, kind);
        if (ranked.Count == 0)
        {
            return items as IReadOnlyList<T> ?? [.. items];
        }

        Dictionary<Guid, int> position = ranked.Select((value, index) => (value, index)).ToDictionary(x => x.value, x => x.index);
        return [.. items.Select((item, index) => (item, index)).OrderBy(x => position.TryGetValue(id(x.item), out int p) ? p : ranked.Count + x.index).Select(x => x.item)];
    }

    /// <summary>Modifie l'espace de travail (thème, menu, dossiers favoris…) et l'enregistre.</summary>
    public Task<IReadOnlyList<ErrorDetail>> UpdateWorkspaceAsync(Func<WorkspacePreferencesDto, WorkspacePreferencesDto> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return SaveAsync(current => (change(current.Workspace), current.EntryDefaults));
    }

    /// <summary>Modifie les valeurs de saisie d'un dossier (« Retenir par défaut ») et les enregistre.</summary>
    public Task<IReadOnlyList<ErrorDetail>> UpdateDefaultsAsync(Guid dossierId, Func<EntryDefaultsDto, EntryDefaultsDto> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return SaveAsync(current =>
        {
            EntryDefaultsDto updated = change(current.EntryDefaults.FirstOrDefault(d => d.DossierId == dossierId) ?? new EntryDefaultsDto { DossierId = dossierId }) with { DossierId = dossierId };
            return (current.Workspace, [.. current.EntryDefaults.Where(d => d.DossierId != dossierId), updated]);
        });
    }

    /// <summary>Remplace tout ce que montre la page « Mes préférences ».</summary>
    public Task<IReadOnlyList<ErrorDetail>> ReplaceAsync(WorkspacePreferencesDto workspace, IReadOnlyList<EntryDefaultsDto> entryDefaults) =>
        SaveAsync(_ => (workspace, entryDefaults));

    /// <summary>Compte un choix (client, article, pièce ouverte). Sans réponse du serveur, rien n'est perdu à l'écran.</summary>
    public async Task RecordUseAsync(Guid? dossierId, UsageKindDto kind, Guid id)
    {
        if (dossierId is not Guid dossier || dossier == Guid.Empty || id == Guid.Empty)
        {
            return;
        }

        await EnsureLoadedAsync().ConfigureAwait(false);
        await WriteAsync(
            gateway => gateway.RecordUsageAsync(new RecordUsageRequest(dossier, kind, id), CancellationToken.None),
            current => current with { Usage = LocalUsage.Record(current.Usage, dossier, kind, id, _clock.GetUtcNow()) }).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ErrorDetail>> ForgetUsageAsync(Guid? dossierId) =>
        WriteAsync(
            gateway => gateway.ForgetUsageAsync(dossierId, CancellationToken.None),
            current => current with { Usage = dossierId is Guid id ? [.. current.Usage.Where(u => u.DossierId != id)] : [] });

    /// <summary>Tout revient aux réglages de départ, dans le compte et sur l'appareil.</summary>
    public Task<IReadOnlyList<ErrorDetail>> ResetAsync() =>
        WriteAsync(gateway => gateway.ResetAsync(CancellationToken.None), current => Nothing with { Version = current.Version });

    private async Task LoadAsync(string? user)
    {
        UserPreferencesResponse? local = Read(await _device.ReadAsync(Key(user)).ConfigureAwait(false));
        if (user != _user)
        {
            return;   // un autre utilisateur est arrivé pendant la lecture : sa propre lecture l'emporte
        }

        Current = local ?? Nothing;
        IsLoaded = true;
        Changed?.Invoke(this, EventArgs.Empty);
        if (user is null || _gateway is null)
        {
            return;
        }

        OperationResult<UserPreferencesResponse> read = await _gateway.GetAsync(CancellationToken.None).ConfigureAwait(false);
        if (user != _user)
        {
            return;
        }

        if (!read.IsSuccess || read.Value is null)
        {
            InAccount = false;
            return;
        }

        InAccount = true;
        if (read.Value.Version is null && local is not null && HasContent(local))
        {
            // Premier passage par le compte : ce que l'appareil retenait y est rangé, plutôt que perdu.
            OperationResult<UserPreferencesResponse> moved = await _gateway.SaveAsync(
                new SaveUserPreferencesRequest { Workspace = local.Workspace, EntryDefaults = [.. local.EntryDefaults] }, CancellationToken.None).ConfigureAwait(false);
            await AdoptAsync(moved.IsSuccess && moved.Value is not null ? moved.Value : read.Value, user).ConfigureAwait(false);
            return;
        }

        await AdoptAsync(read.Value, user).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ErrorDetail>> SaveAsync(Func<UserPreferencesResponse, (WorkspacePreferencesDto Workspace, IReadOnlyList<EntryDefaultsDto> Defaults)> change)
    {
        await EnsureLoadedAsync().ConfigureAwait(false);
        IReadOnlyList<ErrorDetail> errors = await WriteAsync(
            gateway =>
            {
                (WorkspacePreferencesDto workspace, IReadOnlyList<EntryDefaultsDto> defaults) = change(Current);
                return gateway.SaveAsync(new SaveUserPreferencesRequest { Workspace = workspace, EntryDefaults = [.. defaults], Version = Current.Version }, CancellationToken.None);
            },
            current =>
            {
                (WorkspacePreferencesDto workspace, IReadOnlyList<EntryDefaultsDto> defaults) = change(current);
                return current with { Workspace = workspace, EntryDefaults = defaults };
            }).ConfigureAwait(false);
        return errors;
    }

    /// <summary>
    /// Une écriture : dans le compte quand il est disponible (un refus pour version périmée relit puis refait le
    /// changement, une fois), sinon sur l'appareil seulement. Les écritures passent une à une.
    /// </summary>
    private async Task<IReadOnlyList<ErrorDetail>> WriteAsync(
        Func<IUserPreferencesGateway, Task<OperationResult<UserPreferencesResponse>>> remote,
        Func<UserPreferencesResponse, UserPreferencesResponse> local)
    {
        await _writing.WaitAsync().ConfigureAwait(false);
        try
        {
            string? user = _user;
            if (InAccount && _gateway is not null)
            {
                OperationResult<UserPreferencesResponse> written = await remote(_gateway).ConfigureAwait(false);
                if (written.IsStale)
                {
                    OperationResult<UserPreferencesResponse> reread = await _gateway.GetAsync(CancellationToken.None).ConfigureAwait(false);
                    if (reread.IsSuccess && reread.Value is not null)
                    {
                        Current = reread.Value;
                        written = await remote(_gateway).ConfigureAwait(false);
                    }
                }

                if (written.IsSuccess && written.Value is not null)
                {
                    await AdoptAsync(written.Value, user).ConfigureAwait(false);
                    return [];
                }

                if (!written.Errors.Any(e => e.Code is ErrorCodes.Preferences.Unavailable or ErrorCodes.Api.Unreachable))
                {
                    return written.Errors;
                }

                // Le compte ne répond plus : le changement reste sur l'appareil, il sera rangé au prochain passage.
                InAccount = false;
            }

            await AdoptAsync(local(Current), user).ConfigureAwait(false);
            return [];
        }
        finally
        {
            _writing.Release();
        }
    }

    private async Task AdoptAsync(UserPreferencesResponse preferences, string? user)
    {
        if (user != _user)
        {
            return;
        }

        Current = preferences;
        Changed?.Invoke(this, EventArgs.Empty);
        await _device.WriteAsync(Key(user), JsonSerializer.Serialize(preferences, ContractsJsonContext.Default.UserPreferencesResponse)).ConfigureAwait(false);
    }

    private static UserPreferencesResponse? Read(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(stored, ContractsJsonContext.Default.UserPreferencesResponse);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool HasContent(UserPreferencesResponse preferences) =>
        preferences.EntryDefaults.Count > 0
        || preferences.Workspace.NavFavorites.Count > 0
        || preferences.Workspace.CollapsedSections.Count > 0
        || preferences.Workspace.FavoriteDossiers.Count > 0
        || preferences.Workspace.Theme is not null
        || preferences.Workspace.Density is not null
        || preferences.Workspace.HomePage is not null;

    private static string Key(string? user) => KeyPrefix + UserStorageKey.Hash(user);

    private static bool IsDocument(UsageKindDto kind) => kind is UsageKindDto.Invoice or UsageKindDto.Quote;

    private static IEnumerable<UsageEntryDto> Order(IEnumerable<UsageEntryDto> entries, UsageKindDto kind) =>
        IsDocument(kind)
            ? entries.OrderByDescending(e => e.LastUsed)
            : entries.OrderByDescending(e => e.Uses).ThenByDescending(e => e.LastUsed).ThenBy(e => e.Id);

    /// <summary>
    /// Le compte des choix quand les préférences restent sur l'appareil (poste isolé) : mêmes règles que le serveur
    /// (<c>UserPreferences.RecordUse</c>), que l'interface ne peut pas appeler directement.
    /// </summary>
    internal static class LocalUsage
    {
        public static IReadOnlyList<DossierUsageDto> Record(IReadOnlyList<DossierUsageDto> usage, Guid dossierId, UsageKindDto kind, Guid id, DateTimeOffset now)
        {
            DossierUsageDto current = usage.FirstOrDefault(u => u.DossierId == dossierId) ?? new DossierUsageDto(dossierId, []);
            UsageEntryDto? previous = current.Entries.FirstOrDefault(e => e.Kind == kind && e.Id == id);
            UsageEntryDto entry = new(kind, id, (previous?.Uses ?? 0) + 1, now);
            List<UsageEntryDto> entries = [.. current.Entries.Where(e => e != previous), entry];
            List<UsageEntryDto> kept = [];
            foreach (IGrouping<UsageKindDto, UsageEntryDto> group in entries.GroupBy(e => e.Kind))
            {
                kept.AddRange(Order(group, group.Key).Take(IsDocument(group.Key) ? MaxDocumentsPerKind : MaxChoicesPerKind));
            }

            return [new DossierUsageDto(dossierId, kept), .. usage.Where(u => u.DossierId != dossierId)];
        }
    }
}
