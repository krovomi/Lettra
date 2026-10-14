using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Identity.Local;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Identity.Tests;

/// <summary>Mode 1 (annexe F §2) : compte local à phrase secrète, aucun accès avant le déverrouillage.</summary>
public sealed class LocalAccountsTests : IDisposable
{
    private const string Passphrase = "une phrase secrète solide";
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());

    private readonly string _folder;
    private readonly bool _ownsFolder;
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero));
    private readonly ServiceProvider _services;

    public LocalAccountsTests()
        : this(Directory.CreateTempSubdirectory("lettra-local-").FullName, ownsFolder: true)
    {
    }

    private LocalAccountsTests(string folder, bool ownsFolder)
    {
        _folder = folder;
        _ownsFolder = ownsFolder;
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lettra:LocalIdentity:StorePath"] = StorePath,
            ["Lettra:LocalIdentity:Pbkdf2Iterations"] = "1000",
        }).Build();
        ServiceCollection services = new();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton<ITenantCatalog>(new OneDossierCatalog(Dossier));
        services.AddLocalIdentity(configuration);
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private string StorePath => Path.Combine(_folder, "compte.json");

    private ILocalAccounts Accounts => _services.GetRequiredService<ILocalAccounts>();

    private ICurrentUser User => _services.GetRequiredService<ICurrentUser>();

    private async Task<IReadOnlySet<Permission>> PermissionsOnDossierAsync()
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPermissionService>().GetEffectiveAsync(User.UserId, Dossier, default);
    }

    [Fact]
    public async Task Avant_le_premier_lancement_personne_n_est_connecte()
    {
        (await Accounts.GetStatusAsync(default)).Exists.Should().BeFalse();
        User.IsAuthenticated.Should().BeFalse();
        (await PermissionsOnDossierAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Creer_le_compte_ouvre_la_session_avec_les_droits_du_proprietaire()
    {
        (await Accounts.CreateAsync("Claire Martin", Passphrase, default)).IsSuccess.Should().BeTrue();

        User.IsAuthenticated.Should().BeTrue();
        (await PermissionsOnDossierAsync()).Should().Contain([Permission.FactureEmettre, Permission.FecExporter, Permission.UtilisateursGerer]);
    }

    [Fact]
    public async Task Le_fichier_ne_contient_jamais_la_phrase_secrete()
    {
        await Accounts.CreateAsync("Claire Martin", Passphrase, default);

        string stored = await File.ReadAllTextAsync(StorePath);
        stored.Should().NotContain(Passphrase).And.NotContain("secrète").And.Contain("Claire Martin");
    }

    [Fact]
    public async Task Un_second_compte_est_refuse()
    {
        await Accounts.CreateAsync("Claire", Passphrase, default);

        Result again = await Accounts.CreateAsync("Intrus", "une autre phrase longue", default);

        again.Errors.Should().ContainSingle().Which.Code.Should().Be(LocalAccountErrors.AlreadyExistsCode);
    }

    [Fact]
    public async Task Une_phrase_trop_courte_est_refusee() =>
        (await Accounts.CreateAsync("Claire", "court", default)).Errors.Single().Code.Should().Be(LocalAccountErrors.PassphraseTooShortCode);

    [Fact]
    public async Task Verrouiller_puis_deverrouiller()
    {
        await Accounts.CreateAsync("Claire", Passphrase, default);
        Accounts.Lock();
        User.IsAuthenticated.Should().BeFalse();

        (await Accounts.UnlockAsync("mauvaise phrase", default)).Errors.Single().Code.Should().Be(LocalAccountErrors.InvalidPassphraseCode);
        User.IsAuthenticated.Should().BeFalse();

        (await Accounts.UnlockAsync(Passphrase, default)).IsSuccess.Should().BeTrue();
        User.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task La_session_se_ferme_apres_30_minutes_d_inactivite()
    {
        await Accounts.CreateAsync("Claire", Passphrase, default);

        _clock.Advance(TimeSpan.FromMinutes(29));
        User.IsAuthenticated.Should().BeTrue("l'activité repousse l'échéance");
        _clock.Advance(TimeSpan.FromMinutes(30));

        User.IsAuthenticated.Should().BeFalse();
        (await PermissionsOnDossierAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Cinq_echecs_verrouillent_le_poste_de_plus_en_plus_longtemps()
    {
        await Accounts.CreateAsync("Claire", Passphrase, default);
        Accounts.Lock();

        for (int i = 0; i < 4; i++)
        {
            (await Accounts.UnlockAsync("faux", default)).Errors.Single().Code.Should().Be(LocalAccountErrors.InvalidPassphraseCode);
        }

        (await Accounts.UnlockAsync("faux", default)).Errors.Single().Code.Should().Be(LocalAccountErrors.LockedOutCode);
        (await Accounts.UnlockAsync(Passphrase, default)).Errors.Single().Code.Should().Be(LocalAccountErrors.LockedOutCode, "même la bonne phrase attend la fin du verrouillage");

        _clock.Advance(TimeSpan.FromMinutes(1));
        for (int i = 0; i < 5; i++)
        {
            await Accounts.UnlockAsync("faux", default);
        }

        _clock.Advance(TimeSpan.FromMinutes(1));
        (await Accounts.UnlockAsync(Passphrase, default)).Errors.Single().Code.Should().Be(LocalAccountErrors.LockedOutCode, "le deuxième verrouillage dure deux minutes");

        _clock.Advance(TimeSpan.FromMinutes(1));
        (await Accounts.UnlockAsync(Passphrase, default)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Le_compte_survit_au_redemarrage()
    {
        await Accounts.CreateAsync("Claire", Passphrase, default);
        Guid userId = User.UserId;

        using LocalAccountsTests restarted = new(_folder, ownsFolder: false);
        (await restarted.Accounts.GetStatusAsync(default)).Should().Be(new LocalAccountStatus(true, false, "Claire"));
        (await restarted.Accounts.UnlockAsync(Passphrase, default)).IsSuccess.Should().BeTrue();
        restarted.User.UserId.Should().Be(userId);
    }

    public void Dispose()
    {
        _services.Dispose();
        if (_ownsFolder && Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }

    private sealed class OneDossierCatalog(DossierId dossier) : ITenantCatalog
    {
        public ValueTask<TenantDescriptor?> FindAsync(DossierId dossierId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(dossierId == dossier
                ? new TenantDescriptor(dossier, new OrganisationId(Guid.Empty), DatabaseProvider.Sqlite, "config:x")
                : null);

        public ValueTask<bool> UserHasAccessAsync(Guid userId, DossierId dossierId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);
    }
}
