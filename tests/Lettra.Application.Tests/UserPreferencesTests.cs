using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Preferences;
using Lettra.Application.Features.Preferences;
using Lettra.Contracts.Common;
using Lettra.Contracts.Preferences;
using Lettra.Domain.Common;
using Lettra.Domain.Preferences;

namespace Lettra.Application.Tests;

/// <summary>Chacun ne lit et n'écrit que ses propres préférences, à partir de la version lue.</summary>
public sealed class UserPreferencesTests
{
    private static readonly Guid Dossier = Guid.CreateVersion7();

    private readonly FakeUser _user = new();
    private readonly InMemoryPreferencesStore _store = new();

    private Task<Result<UserPreferencesResponse>> Get() =>
        new GetUserPreferencesHandler(_store, _user).HandleAsync(new GetUserPreferencesQuery(), CancellationToken.None);

    private Task<Result<UserPreferencesResponse>> Save(SaveUserPreferencesRequest request) =>
        new SaveUserPreferencesHandler(_store, _user, TimeProvider.System).HandleAsync(new SaveUserPreferencesCommand(request), CancellationToken.None);

    private Task<Result<UserPreferencesResponse>> Record(Guid id, UsageKindDto kind = UsageKindDto.Customer) =>
        new RecordUsageHandler(_store, _user, TimeProvider.System).HandleAsync(new RecordUsageCommand(new RecordUsageRequest(Dossier, kind, id)), CancellationToken.None);

    private Task<Result<UserPreferencesResponse>> Forget(Guid? dossier) =>
        new ForgetUsageHandler(_store, _user, TimeProvider.System).HandleAsync(new ForgetUsageCommand(dossier), CancellationToken.None);

    private Task<Result<UserPreferencesResponse>> Reset() =>
        new ResetUserPreferencesHandler(_store, _user, TimeProvider.System).HandleAsync(new ResetUserPreferencesCommand(), CancellationToken.None);

    private static SaveUserPreferencesRequest Request(long? version, string theme = "Dark") => new()
    {
        Workspace = new WorkspacePreferencesDto { Theme = theme, Language = "en-US", HomePage = "billing/invoices", NavFavorites = ["billing/quotes"] },
        EntryDefaults = [new EntryDefaultsDto { DossierId = Dossier, VatRateCode = "FR-20", Unit = "HUR", QuoteValidityDays = 45 }],
        Version = version,
    };

    [Fact]
    public async Task Un_compte_neuf_lit_des_preferences_vides_sans_version()
    {
        Result<UserPreferencesResponse> read = await Get();

        read.IsSuccess.Should().BeTrue();
        read.Value.Version.Should().BeNull();
        read.Value.UpdatedAt.Should().BeNull();
        read.Value.Workspace.RankByUsage.Should().BeTrue();
        read.Value.EntryDefaults.Should().BeEmpty();
        read.Value.Usage.Should().BeEmpty();
    }

    [Fact]
    public async Task Enregistrer_puis_relire_rend_les_memes_reglages()
    {
        Result<UserPreferencesResponse> saved = await Save(Request(null));

        saved.IsSuccess.Should().BeTrue();
        saved.Value.Version.Should().Be(1);
        Result<UserPreferencesResponse> read = await Get();
        read.Value.Should().BeEquivalentTo(saved.Value);
        read.Value.Workspace.Theme.Should().Be("Dark");
        read.Value.Workspace.Language.Should().Be("en-US");
        read.Value.EntryDefaults.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new EntryDefaultsDto { DossierId = Dossier, VatRateCode = "FR-20", Unit = "HUR", QuoteValidityDays = 45 });
        _store.Rows.Keys.Should().Equal(_user.UserId);
    }

    [Fact]
    public async Task Une_version_depassee_est_refusee()
    {
        await Save(Request(null));
        await Save(Request(1, "Light"));

        Result<UserPreferencesResponse> stale = await Save(Request(1, "Dark"));

        stale.Errors.Should().ContainSingle().Which.Code.Should().Be(ConcurrencyErrors.VersionMismatchCode);
        (await Get()).Value.Workspace.Theme.Should().Be("Light");
    }

    [Fact]
    public async Task Un_reglage_invalide_est_refuse_et_rien_n_est_ecrit()
    {
        SaveUserPreferencesRequest request = Request(null) with { Workspace = new WorkspacePreferencesDto { HomePage = "https://ailleurs.example" } };

        Result<UserPreferencesResponse> saved = await Save(request);

        saved.Errors.Should().ContainSingle().Which.Should().Be(UserPreferencesErrors.Invalid(nameof(WorkspacePreferences.HomePage)));
        _store.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Les_choix_comptes_sont_fusionnes_sans_version_et_ranges_par_usage()
    {
        Guid souvent = Guid.CreateVersion7();
        Guid parfois = Guid.CreateVersion7();
        await Save(Request(null));

        await Record(parfois);
        await Record(souvent);
        Result<UserPreferencesResponse> recorded = await Record(souvent);

        recorded.IsSuccess.Should().BeTrue();
        recorded.Value.Workspace.Theme.Should().Be("Dark", "compter un choix ne touche pas aux réglages");
        DossierUsageDto usage = recorded.Value.Usage.Should().ContainSingle().Subject;
        usage.DossierId.Should().Be(Dossier);
        usage.Entries.OrderByDescending(e => e.Uses).Select(e => (e.Kind, e.Id, e.Uses))
            .Should().Equal((UsageKindDto.Customer, souvent, 2), (UsageKindDto.Customer, parfois, 1));
    }

    [Fact]
    public async Task Un_autre_appareil_qui_ecrit_entre_temps_ne_fait_pas_perdre_le_choix()
    {
        _store.ConflictsBeforeSave = 2;

        Result<UserPreferencesResponse> recorded = await Record(Guid.CreateVersion7());

        recorded.IsSuccess.Should().BeTrue();
        _store.SaveCalls.Should().Be(3);
    }

    [Fact]
    public async Task Apres_trois_conflits_l_ecriture_est_rendue_en_conflit()
    {
        _store.ConflictsBeforeSave = UserPreferencesAccess.MaxAttempts;

        Result<UserPreferencesResponse> recorded = await Record(Guid.CreateVersion7());

        recorded.Errors.Should().ContainSingle().Which.Code.Should().Be(ConcurrencyErrors.VersionMismatchCode);
        _store.SaveCalls.Should().Be(UserPreferencesAccess.MaxAttempts);
    }

    [Fact]
    public async Task Un_choix_invalide_est_refuse()
    {
        (await Record(Guid.Empty)).Errors.Should().ContainSingle().Which.Code.Should().Be(UserPreferencesErrors.InvalidCode);
        _store.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task Oublier_les_choix_puis_tout_effacer()
    {
        await Save(Request(null));
        await Record(Guid.CreateVersion7());

        Result<UserPreferencesResponse> forgotten = await Forget(Dossier);
        forgotten.Value.Usage.Should().BeEmpty();
        forgotten.Value.Workspace.Theme.Should().Be("Dark");

        await Record(Guid.CreateVersion7());
        (await Forget(null)).Value.Usage.Should().BeEmpty();

        Result<UserPreferencesResponse> reset = await Reset();
        reset.IsSuccess.Should().BeTrue();
        reset.Value.Workspace.Theme.Should().BeNull();
        reset.Value.EntryDefaults.Should().BeEmpty();
        reset.Value.Version.Should().NotBeNull("le compte garde sa ligne, vide, pour que les autres appareils voient l'effacement");
    }

    [Fact]
    public async Task Sans_session_rien_n_est_lu_ni_ecrit()
    {
        _user.IsAuthenticated = false;

        (await Get()).Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.UnauthenticatedCode);
        (await Save(Request(null))).Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.UnauthenticatedCode);
        (await Record(Guid.CreateVersion7())).Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.UnauthenticatedCode);
        _store.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_identifiant_vide_vaut_une_absence_de_session()
    {
        _user.UserId = Guid.Empty;

        (await Get()).Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.UnauthenticatedCode);
    }

    [Fact]
    public async Task Sans_catalogue_les_preferences_sont_indisponibles()
    {
        _store.IsAvailable = false;

        (await Get()).Errors.Should().ContainSingle().Which.Code.Should().Be(UserPreferencesErrors.UnavailableCode);
        (await Save(Request(null))).Errors.Should().ContainSingle().Which.Code.Should().Be(UserPreferencesErrors.UnavailableCode);
        (await Reset()).Errors.Should().ContainSingle().Which.Code.Should().Be(UserPreferencesErrors.UnavailableCode);
    }

    [Fact]
    public async Task Le_magasin_par_defaut_n_ecrit_rien()
    {
        NoUserPreferencesStore none = new();

        none.IsAvailable.Should().BeFalse();
        (await none.FindAsync(Guid.CreateVersion7(), CancellationToken.None)).Should().BeNull();
        (await none.SaveAsync(UserPreferences.Empty(Guid.CreateVersion7()), CancellationToken.None))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(UserPreferencesErrors.UnavailableCode);
    }

    [Fact]
    public void Les_requetes_incompletes_sont_refusees()
    {
        SaveUserPreferencesValidator save = new();
        save.Validate(new SaveUserPreferencesCommand(null!)).Select(e => e.Target).Should().Equal("Request");
        save.Validate(new SaveUserPreferencesCommand(new SaveUserPreferencesRequest { Workspace = null!, EntryDefaults = [null!] }))
            .Select(e => e.Target).Should().Equal("Workspace", "EntryDefaults");
        save.Validate(new SaveUserPreferencesCommand(Request(null))).Should().BeEmpty();

        RecordUsageValidator record = new();
        record.Validate(new RecordUsageCommand(null!)).Select(e => e.Code).Should().Equal(ErrorCodes.Validation.Required);
        record.Validate(new RecordUsageCommand(new RecordUsageRequest(Dossier, UsageKindDto.Quote, Guid.CreateVersion7()))).Should().BeEmpty();
    }

    /// <summary>Une ligne par compte, version avancée à chaque écriture, conflits simulables.</summary>
    private sealed class InMemoryPreferencesStore : IUserPreferencesStore
    {
        public Dictionary<Guid, UserPreferences> Rows { get; } = [];

        public bool IsAvailable { get; set; } = true;

        public int ConflictsBeforeSave { get; set; }

        public int SaveCalls { get; private set; }

        public ValueTask<UserPreferences?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Rows.GetValueOrDefault(userId));

        public ValueTask<Result<UserPreferences>> SaveAsync(UserPreferences preferences, CancellationToken cancellationToken)
        {
            SaveCalls++;
            long current = Rows.GetValueOrDefault(preferences.UserId)?.Version ?? 0;
            if (ConflictsBeforeSave > 0)
            {
                ConflictsBeforeSave--;
                return ValueTask.FromResult<Result<UserPreferences>>(ConcurrencyErrors.VersionMismatch);
            }

            if (current != preferences.Version)
            {
                return ValueTask.FromResult<Result<UserPreferences>>(ConcurrencyErrors.VersionMismatch);
            }

            UserPreferences saved = UserPreferences.Restore(
                preferences.UserId, preferences.Workspace, preferences.EntryDefaults, preferences.Usage, current + 1, preferences.UpdatedAt);
            Rows[preferences.UserId] = saved;
            return ValueTask.FromResult<Result<UserPreferences>>(saved);
        }
    }
}
