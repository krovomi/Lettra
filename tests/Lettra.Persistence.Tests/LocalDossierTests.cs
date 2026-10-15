using System.Security.Cryptography;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Bootstrap;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Sqlite;
using Lettra.Infrastructure.Persistence.Sqlite.Local;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>Poste isolé (mode 1) : registre local, base chiffrée par sa DEK, clé du cabinet dans le coffre.</summary>
public sealed class LocalDossierTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lettra-poste-").FullName;
    private readonly InMemoryKekStore _keys = new();

    private ServiceProvider Poste()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [LettraBootstrap.DeploymentModeKey] = "IsolatedDesktop",
            [LettraBootstrap.LocalDataDirectoryKey] = _folder,
        }).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddLettra(configuration);
        services.AddSingleton<IKekStore>(_keys);
        services.AddScoped<IRequestedDossierSource, NoRequestedDossier>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public async Task Premier_lancement_cree_le_cabinet_et_un_dossier_chiffre()
    {
        await using ServiceProvider poste = Poste();
        OrganisationId organisation = await poste.GetRequiredService<LocalDossierStartup>().EnsureReadyAsync(default);
        LocalDossierRegistry registry = poste.GetRequiredService<LocalDossierRegistry>();

        LocalDossierManifest dossier = (await registry.ListAsync(default)).Should().ContainSingle().Subject;
        dossier.Name.Should().Be(LocalDossierStartup.FirstDossierName);
        dossier.OrganisationId.Should().Be(organisation.Value);
        registry.ActiveDossierId.Should().Be(dossier.DossierId);

        // Sans la clé, le fichier n'est pas une base SQLite lisible.
        Action plain = () =>
        {
            using SqliteConnection connection = new($"Data Source={dossier.DatabaseFile};Pooling=False");
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM sqlite_master";
            command.ExecuteScalar();
        };
        plain.Should().Throw<SqliteException>();

        string connectionString = await poste.GetRequiredService<ISecretStore>()
            .GetConnectionStringAsync(LocalDossierRegistry.SecretRef(dossier.DossierId), default);
        SqliteEncryption.CipherOf(connectionString).Should().Be("chacha20");

        // Second lancement : rien de recréé.
        await poste.GetRequiredService<LocalDossierStartup>().EnsureReadyAsync(default);
        (await registry.ListAsync(default)).Should().ContainSingle();
    }

    [Fact]
    public async Task Poste_relie_cree_la_replique_du_dossier_du_serveur_avec_son_identifiant()
    {
        Guid serveur = Guid.CreateVersion7();
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [LettraBootstrap.DeploymentModeKey] = "HybridCloud",
            [LettraBootstrap.LocalDataDirectoryKey] = _folder,
            [LettraBootstrap.ReplicaDossierIdKey] = serveur.ToString(),
        }).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddLettra(configuration);
        services.AddSingleton<IKekStore>(_keys);
        services.AddScoped<IRequestedDossierSource, NoRequestedDossier>();
        await using ServiceProvider poste = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await poste.InitializeLettraAsync(configuration, default);
        LocalDossierRegistry registry = poste.GetRequiredService<LocalDossierRegistry>();

        // Seulement la réplique, sous l'identifiant du serveur, chiffrée comme en mode 1 et ouverte.
        LocalDossierManifest replique = (await registry.ListAsync(default)).Should().ContainSingle().Subject;
        replique.DossierId.Should().Be(serveur);
        replique.Name.Should().Be(LocalDossierStartup.ReplicaName);
        registry.ActiveDossierId.Should().Be(serveur);
        string connectionString = await poste.GetRequiredService<ISecretStore>()
            .GetConnectionStringAsync(LocalDossierRegistry.SecretRef(serveur), default);
        SqliteEncryption.CipherOf(connectionString).Should().Be("chacha20");

        // Second lancement : la même réplique est rouverte, rien n'est recréé.
        await poste.InitializeLettraAsync(configuration, default);
        (await registry.ListAsync(default)).Should().ContainSingle().Which.WrappedDek.Should().Equal(replique.WrappedDek);
    }

    [Fact]
    public async Task Poste_relie_sans_dossier_du_serveur_ne_demarre_pas()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [LettraBootstrap.DeploymentModeKey] = "HybridCloud",
            [LettraBootstrap.LocalDataDirectoryKey] = _folder,
        }).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddLettra(configuration);
        services.AddSingleton<IKekStore>(_keys);
        services.AddScoped<IRequestedDossierSource, NoRequestedDossier>();
        await using ServiceProvider poste = services.BuildServiceProvider();

        Func<Task> start = () => poste.InitializeLettraAsync(configuration, default);
        await start.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{LettraBootstrap.ReplicaDossierIdKey}*");
    }

    [Fact]
    public async Task Detruire_la_cle_rend_le_dossier_illisible()
    {
        await using ServiceProvider poste = Poste();
        await poste.GetRequiredService<LocalDossierStartup>().EnsureReadyAsync(default);
        LocalDossierRegistry registry = poste.GetRequiredService<LocalDossierRegistry>();
        Guid dossier = registry.ActiveDossierId!.Value;

        await registry.DestroyKeyAsync(dossier, default);

        Func<Task> open = () => poste.GetRequiredService<ISecretStore>().GetConnectionStringAsync(LocalDossierRegistry.SecretRef(dossier), default).AsTask();
        await open.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task Le_dossier_ouvert_est_celui_du_poste()
    {
        await using ServiceProvider poste = Poste();
        OrganisationId organisation = await poste.GetRequiredService<LocalDossierStartup>().EnsureReadyAsync(default);

        Domain.Common.Result<DossierId> second = await poste.GetRequiredService<IDossierProvisioner>().CreateAsync(organisation, "Second dossier", default);

        second.IsSuccess.Should().BeTrue();
        poste.GetRequiredService<LocalDossierRegistry>().ActiveDossierId.Should().Be(second.Value.Value);
        (await poste.GetRequiredService<IDossierDirectory>().ListAsync(organisation, default)).Select(d => d.Name)
            .Should().Equal("Dossier principal", "Second dossier");
        (await poste.GetRequiredService<ITenantCatalog>().FindAsync(second.Value, default))!.Provider.Should().Be(DatabaseProvider.Sqlite);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }
}

public sealed class DossierKeyringTests
{
    private readonly InMemoryKekStore _keys = new();

    private IDossierKeyring Keyring()
    {
        ServiceCollection services = new();
        services.AddSingleton<IKekStore>(_keys);
        services.AddPersistenceCore();
        return services.BuildServiceProvider().GetRequiredService<IDossierKeyring>();
    }

    [Fact]
    public async Task La_DEK_n_est_lisible_que_pour_son_dossier()
    {
        IDossierKeyring keyring = Keyring();
        OrganisationId cabinet = new(Guid.CreateVersion7());
        DossierId a = new(Guid.CreateVersion7());
        byte[] wrapped = await keyring.CreateWrappedDekAsync(cabinet, a, default);

        (await keyring.UnwrapDekAsync(cabinet, a, wrapped, default)).Should().HaveCount(32);
        Func<Task> other = () => keyring.UnwrapDekAsync(cabinet, new DossierId(Guid.CreateVersion7()), wrapped, default).AsTask();
        await other.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task Sans_la_cle_du_cabinet_le_dossier_est_illisible()
    {
        IDossierKeyring keyring = Keyring();
        OrganisationId cabinet = new(Guid.CreateVersion7());
        DossierId a = new(Guid.CreateVersion7());
        byte[] wrapped = await keyring.CreateWrappedDekAsync(cabinet, a, default);

        _keys.Forget(cabinet);

        Func<Task> unwrap = () => keyring.UnwrapDekAsync(cabinet, a, wrapped, default).AsTask();
        await unwrap.Should().ThrowAsync<CryptographicException>();
    }
}
