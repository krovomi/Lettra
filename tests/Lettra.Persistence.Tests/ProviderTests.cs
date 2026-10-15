using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.PostgreSql;
using Lettra.Infrastructure.Persistence.Sqlite;
using Lettra.Infrastructure.Persistence.SqlServer;
using Lettra.Infrastructure.Catalog;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace Lettra.Persistence.Tests;

public sealed class SqliteTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lettra-sqlite-").FullName;

    private string Database(string name) => $"Data Source={Path.Combine(_folder, name)}.db;Pooling=False";

    [Fact]
    public async Task Aller_retour_d_un_client()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        await CustomerPersistenceScenarios.RoundTripAsync(harness, DatabaseProvider.Sqlite, Database("a"));
    }

    [Fact]
    public async Task Journal_de_synchronisation_reception_outbox_et_reponses_gardees()
    {
        await using TenantHarness harness = new(new SqliteConfigurator(), syncJournal: true);
        await SyncPersistenceScenarios.JournalFeedOutboxAndReceiptsAsync(harness, DatabaseProvider.Sqlite, Database("sync"));
    }

    [Fact]
    public async Task Outbox_ecrite_avec_le_changement_metier_et_envoyee_aussitot()
    {
        FakeRecipient recipient = new();
        await using TenantHarness harness = OutboxScenarios.Harness(new SqliteConfigurator(), new ManualClock(OutboxScenarios.Start), recipient);
        await OutboxScenarios.SameTransactionAsync(harness, recipient, DatabaseProvider.Sqlite, Database("outbox"));
    }

    [Fact]
    public async Task Outbox_reprises_espacees_puis_mise_de_cote()
    {
        FakeRecipient recipient = new();
        ManualClock clock = new(OutboxScenarios.Start);
        await using TenantHarness harness = OutboxScenarios.Harness(new SqliteConfigurator(), clock, recipient);
        await OutboxScenarios.RetryBackoffAndParkingAsync(harness, clock, recipient, DatabaseProvider.Sqlite, Database("outbox-reprise"));
    }

    [Fact]
    public async Task Outbox_reprise_apres_un_arret_en_plein_envoi()
    {
        FakeRecipient recipient = new();
        ManualClock clock = new(OutboxScenarios.Start);
        await using TenantHarness harness = OutboxScenarios.Harness(new SqliteConfigurator(), clock, recipient);
        await OutboxScenarios.CrashRecoveryAsync(harness, clock, recipient, DatabaseProvider.Sqlite, Database("outbox-arret"));
    }

    [Fact]
    public async Task Outbox_envoyee_par_le_Worker_seul()
    {
        FakeRecipient recipient = new();
        await using TenantHarness harness = OutboxScenarios.Harness(new SqliteConfigurator(), new ManualClock(OutboxScenarios.Start), recipient, inline: false);
        await OutboxScenarios.WorkerOnlyAsync(harness, recipient, DatabaseProvider.Sqlite, Database("outbox-a"), Database("outbox-b"));
    }

    [Fact]
    public async Task Series_de_poste_ouvertes_recues_dans_l_ordre_et_installees_sur_le_poste()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        await SyncPersistenceScenarios.DeviceSeriesAsync(harness, DatabaseProvider.Sqlite, Database("serie"), Database("serie-poste"));
    }

    [Fact]
    public async Task Une_base_par_dossier()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        await CustomerPersistenceScenarios.IsolationAsync(harness, DatabaseProvider.Sqlite, Database("a"), Database("b"));
    }

    [Fact]
    public async Task Aller_retour_d_un_fournisseur()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        await PartiesAndCatalogPersistenceScenarios.SupplierRoundTripAsync(harness, DatabaseProvider.Sqlite, Database("f"));
    }

    [Fact]
    public async Task Aller_retour_d_un_article_du_catalogue()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        await PartiesAndCatalogPersistenceScenarios.CatalogRoundTripAsync(harness, DatabaseProvider.Sqlite, Database("g"));
    }

    [Fact]
    public async Task Concurrence_optimiste_sur_le_catalogue()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        await PartiesAndCatalogPersistenceScenarios.OptimisticConcurrencyAsync(harness, DatabaseProvider.Sqlite, Database("conc"));
    }

    [Fact]
    public async Task Recherche_et_unicite_du_catalogue()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        await PartiesAndCatalogPersistenceScenarios.CatalogSearchAndUniquenessAsync(harness, DatabaseProvider.Sqlite, Database("h"));
    }

    [Fact]
    public async Task Recherche_sans_accents_sur_les_tiers_et_le_catalogue()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        await AccentInsensitiveSearchScenarios.SearchIgnoresAccentsAndCaseAsync(harness, DatabaseProvider.Sqlite, Database("rsa"));
    }

    [Fact]
    public async Task Le_remplissage_de_la_migration_donne_la_meme_recherche()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        await AccentInsensitiveSearchScenarios.BackfillMatchesTheCSharpFormAsync(harness, DatabaseProvider.Sqlite, Database("rsb"));
    }

    [Fact]
    public async Task Numerotation_continue_sous_concurrence()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        await TenancyScenarios.NumberingAsync(harness, DatabaseProvider.Sqlite, Database("num"));
    }

    [Fact]
    public async Task Cycle_de_vie_devis_acompte_facture_finale_et_avoir()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        await BillingPersistenceScenarios.LifecycleAsync(harness, DatabaseProvider.Sqlite, Database("devis"));
    }

    [Fact]
    public async Task Fec_des_pieces_validees()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        await ExportScenarios.FecOfValidatedInvoicesAsync(harness, DatabaseProvider.Sqlite, Database("fec"));
    }

    [Fact]
    public async Task Export_fec_en_masse_avec_reprise()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        await ExportScenarios.BulkExportAsync(harness, DatabaseProvider.Sqlite, Database("fec-a"), Database("fec-b"));
    }

    [Fact]
    public async Task Factures_electroniques_CII_et_UBL_des_pieces_validees()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        await EInvoiceScenarios.SamplesAsync(harness, DatabaseProvider.Sqlite, Database("einvoice"), Database("einvoice-franchise"));
    }

    [Fact]
    public async Task Transmission_a_la_plateforme_agreee_simulee()
    {
        await using TenantHarness harness = new(new SqliteConfigurator(), simulatedPlatform: true);
        await PlatformScenarios.TransmissionAsync(harness, DatabaseProvider.Sqlite, Database("pa"));
    }

    [Fact]
    public async Task Sans_plateforme_agreee_configuree_rien_ne_part()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        await PlatformScenarios.NotConfiguredAsync(harness, DatabaseProvider.Sqlite, Database("pa-aucune"));
    }

    [Fact]
    public async Task Validations_concurrentes_sans_trou_ni_doublon()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        await BillingPersistenceScenarios.ConcurrentValidationsAsync(harness, DatabaseProvider.Sqlite, Database("valid"));
    }

    [Fact]
    public async Task Une_version_perimee_n_ecrase_pas_la_piece()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        await BillingPersistenceScenarios.StaleVersionAsync(harness, DatabaseProvider.Sqlite, Database("version"));
    }

    [Fact]
    public async Task Une_base_d_un_autre_dossier_est_refusee()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        await TenancyScenarios.StampGuardAsync(harness, DatabaseProvider.Sqlite, Database("tampon"));
    }

    [Fact]
    public async Task Validations_concurrentes_dans_la_chaine()
    {
        await using TenantHarness harness = new(new SqliteConfigurator(), syntheticDocuments: true);
        await IntegrityScenarios.ConcurrentValidationsAsync(harness, DatabaseProvider.Sqlite, Database("chaine"));
    }

    [Fact]
    public async Task Une_ligne_modifiee_en_base_est_detectee()
    {
        await using TenantHarness harness = new(new SqliteConfigurator(), syntheticDocuments: true);
        await IntegrityScenarios.TamperDetectedAsync(harness, DatabaseProvider.Sqlite, Database("alteration"));
    }

    [Fact]
    public async Task Clotures_signees_et_archive_scellee()
    {
        ManualClock clock = new(DateTimeOffset.UtcNow);
        await using TenantHarness harness = new(new SqliteConfigurator(), clock, syntheticDocuments: true);
        await IntegrityScenarios.ClosingsAndSealedArchiveAsync(harness, clock, DatabaseProvider.Sqlite, Database("clotures"));
    }

    [Fact]
    public async Task Rotation_des_cles_et_historique_verifiable()
    {
        ManualClock clock = new(DateTimeOffset.UtcNow);
        await using TenantHarness harness = new(new SqliteConfigurator(), clock, syntheticDocuments: true);
        await IntegrityScenarios.KeyRotationAsync(harness, clock, DatabaseProvider.Sqlite, Database("rotation"));
    }

    [Fact]
    public async Task Chaine_d_avant_le_versionnage_adoptee()
    {
        await using TenantHarness harness = new(new SqliteConfigurator(), syntheticDocuments: true);
        await IntegrityScenarios.LegacyChainAdoptedAsync(harness, DatabaseProvider.Sqlite, Database("ancienne"));
    }

    [Fact]
    public async Task Creation_de_dossier_et_vagues_de_migration_par_le_catalogue()
    {
        await using CatalogHarness harness = new(
            s => s.AddSqliteProvider(),
            b => SqliteConfigurator.ConfigureCatalog(b, Database("catalog")),
            new Dictionary<string, string?> { ["Provider"] = "Sqlite", ["Directory"] = Path.Combine(_folder, "srv-a") });
        await CatalogScenarios.ProvisionAndMigrateAsync(harness, DatabaseProvider.Sqlite);
    }

    [Fact]
    public async Task Registre_des_droits_dans_le_catalogue()
    {
        await using CatalogHarness harness = new(
            s => s.AddSqliteProvider(),
            b => SqliteConfigurator.ConfigureCatalog(b, Database("catalog-droits")),
            new Dictionary<string, string?> { ["Provider"] = "Sqlite", ["Directory"] = Path.Combine(_folder, "srv-droits") });
        await CatalogScenarios.AuthorizationDirectoryAsync(harness);
    }

    [Fact]
    public async Task Politiques_du_cabinet_dans_le_catalogue()
    {
        await using CatalogHarness harness = new(
            s => s.AddSqliteProvider(),
            b => SqliteConfigurator.ConfigureCatalog(b, Database("catalog-politiques")),
            new Dictionary<string, string?> { ["Provider"] = "Sqlite", ["Directory"] = Path.Combine(_folder, "srv-politiques") });
        await CatalogScenarios.OrganisationPoliciesAsync(harness);
    }

    [Fact]
    public async Task Cabinet_inconnu()
    {
        await using CatalogHarness harness = new(
            s => s.AddSqliteProvider(),
            b => SqliteConfigurator.ConfigureCatalog(b, Database("catalog-inconnu")),
            new Dictionary<string, string?> { ["Provider"] = "Sqlite", ["Directory"] = _folder });
        await CatalogScenarios.UnknownOrganisationAsync(harness);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }
}

/// <summary>Docker est-il joignable ? Sinon les tests de conteneurs sont ignorés localement (jamais en CI).</summary>
internal static class Docker
{
    public static readonly bool Available =
        Environment.GetEnvironmentVariable("CI") == "true"
        || File.Exists("/var/run/docker.sock")
        || OperatingSystem.IsWindows();
}

public sealed class PostgreSqlTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public async Task InitializeAsync()
    {
        if (Docker.Available)
        {
            _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await _container.StartAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private string Database(string name) =>
        new Npgsql.NpgsqlConnectionStringBuilder(_container!.GetConnectionString()) { Database = $"dossier_{name}" }.ConnectionString;

    [SkippableFact]
    public async Task Journal_de_synchronisation_reception_outbox_et_reponses_gardees()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator(), syncJournal: true);
        await SyncPersistenceScenarios.JournalFeedOutboxAndReceiptsAsync(harness, DatabaseProvider.PostgreSql, Database("sync"));
    }

    [SkippableFact]
    public async Task Outbox_ecrite_avec_le_changement_metier_et_envoyee_aussitot()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        FakeRecipient recipient = new();
        await using TenantHarness harness = OutboxScenarios.Harness(new PostgreSqlConfigurator(), new ManualClock(OutboxScenarios.Start), recipient);
        await OutboxScenarios.SameTransactionAsync(harness, recipient, DatabaseProvider.PostgreSql, Database("outbox"));
    }

    [SkippableFact]
    public async Task Outbox_reprises_espacees_puis_mise_de_cote()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        FakeRecipient recipient = new();
        ManualClock clock = new(OutboxScenarios.Start);
        await using TenantHarness harness = OutboxScenarios.Harness(new PostgreSqlConfigurator(), clock, recipient);
        await OutboxScenarios.RetryBackoffAndParkingAsync(harness, clock, recipient, DatabaseProvider.PostgreSql, Database("outbox-reprise"));
    }

    [SkippableFact]
    public async Task Outbox_reprise_apres_un_arret_en_plein_envoi()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        FakeRecipient recipient = new();
        ManualClock clock = new(OutboxScenarios.Start);
        await using TenantHarness harness = OutboxScenarios.Harness(new PostgreSqlConfigurator(), clock, recipient);
        await OutboxScenarios.CrashRecoveryAsync(harness, clock, recipient, DatabaseProvider.PostgreSql, Database("outbox-arret"));
    }

    [SkippableFact]
    public async Task Outbox_envoyee_par_le_Worker_seul()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        FakeRecipient recipient = new();
        await using TenantHarness harness = OutboxScenarios.Harness(new PostgreSqlConfigurator(), new ManualClock(OutboxScenarios.Start), recipient, inline: false);
        await OutboxScenarios.WorkerOnlyAsync(harness, recipient, DatabaseProvider.PostgreSql, Database("outbox-a"), Database("outbox-b"));
    }

    [SkippableFact]
    public async Task Series_de_poste_ouvertes_recues_dans_l_ordre_et_installees_sur_le_poste()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator());
        await SyncPersistenceScenarios.DeviceSeriesAsync(harness, DatabaseProvider.PostgreSql, Database("serie"), Database("serie-poste"));
    }

    [SkippableFact]
    public async Task Validations_concurrentes_dans_la_chaine()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator(), syntheticDocuments: true);
        await IntegrityScenarios.ConcurrentValidationsAsync(harness, DatabaseProvider.PostgreSql, Database("chaine"));
    }

    [SkippableFact]
    public async Task Une_ligne_modifiee_en_base_est_detectee()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator(), syntheticDocuments: true);
        await IntegrityScenarios.TamperDetectedAsync(harness, DatabaseProvider.PostgreSql, Database("alteration"));
    }

    [SkippableFact]
    public async Task Clotures_signees_et_archive_scellee()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        ManualClock clock = new(DateTimeOffset.UtcNow);
        await using TenantHarness harness = new(new PostgreSqlConfigurator(), clock, syntheticDocuments: true);
        await IntegrityScenarios.ClosingsAndSealedArchiveAsync(harness, clock, DatabaseProvider.PostgreSql, Database("clotures"));
    }

    [SkippableFact]
    public async Task Rotation_des_cles_et_historique_verifiable()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        ManualClock clock = new(DateTimeOffset.UtcNow);
        await using TenantHarness harness = new(new PostgreSqlConfigurator(), clock, syntheticDocuments: true);
        await IntegrityScenarios.KeyRotationAsync(harness, clock, DatabaseProvider.PostgreSql, Database("rotation"));
    }

    [SkippableFact]
    public async Task Aller_retour_d_un_client()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator());
        await CustomerPersistenceScenarios.RoundTripAsync(harness, DatabaseProvider.PostgreSql, Database("a"));
    }

    [SkippableFact]
    public async Task Une_base_par_dossier()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator());
        await CustomerPersistenceScenarios.IsolationAsync(harness, DatabaseProvider.PostgreSql, Database("b"), Database("c"));
    }

    [SkippableFact]
    public async Task Aller_retour_d_un_fournisseur()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator());
        await PartiesAndCatalogPersistenceScenarios.SupplierRoundTripAsync(harness, DatabaseProvider.PostgreSql, Database("f"));
    }

    [SkippableFact]
    public async Task Aller_retour_d_un_article_du_catalogue()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator());
        await PartiesAndCatalogPersistenceScenarios.CatalogRoundTripAsync(harness, DatabaseProvider.PostgreSql, Database("g"));
    }

    [SkippableFact]
    public async Task Concurrence_optimiste_sur_le_catalogue()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator());
        await PartiesAndCatalogPersistenceScenarios.OptimisticConcurrencyAsync(harness, DatabaseProvider.PostgreSql, Database("conc"));
    }

    [SkippableFact]
    public async Task Recherche_et_unicite_du_catalogue()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator());
        await PartiesAndCatalogPersistenceScenarios.CatalogSearchAndUniquenessAsync(harness, DatabaseProvider.PostgreSql, Database("h"));
    }

    [SkippableFact]
    public async Task Recherche_sans_accents_sur_les_tiers_et_le_catalogue()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator());
        await AccentInsensitiveSearchScenarios.SearchIgnoresAccentsAndCaseAsync(harness, DatabaseProvider.PostgreSql, Database("rsa"));
    }

    [SkippableFact]
    public async Task Le_remplissage_de_la_migration_donne_la_meme_recherche()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator());
        await AccentInsensitiveSearchScenarios.BackfillMatchesTheCSharpFormAsync(harness, DatabaseProvider.PostgreSql, Database("rsb"));
    }

    [SkippableFact]
    public async Task Numerotation_continue_sous_concurrence()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator());
        await TenancyScenarios.NumberingAsync(harness, DatabaseProvider.PostgreSql, Database("num"));
    }

    [SkippableFact]
    public async Task Cycle_de_vie_devis_acompte_facture_finale_et_avoir()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator());
        await BillingPersistenceScenarios.LifecycleAsync(harness, DatabaseProvider.PostgreSql, Database("devis"));
    }

    [SkippableFact]
    public async Task Fec_des_pieces_validees()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator());
        await ExportScenarios.FecOfValidatedInvoicesAsync(harness, DatabaseProvider.PostgreSql, Database("fec"));
    }

    [SkippableFact]
    public async Task Export_fec_en_masse_avec_reprise()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator());
        await ExportScenarios.BulkExportAsync(harness, DatabaseProvider.PostgreSql, Database("fec_a"), Database("fec_b"));
    }

    [SkippableFact]
    public async Task Transmission_a_la_plateforme_agreee_simulee()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator(), simulatedPlatform: true);
        await PlatformScenarios.TransmissionAsync(harness, DatabaseProvider.PostgreSql, Database("pa"));
    }

    [SkippableFact]
    public async Task Validations_concurrentes_sans_trou_ni_doublon()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator());
        await BillingPersistenceScenarios.ConcurrentValidationsAsync(harness, DatabaseProvider.PostgreSql, Database("valid"));
    }

    [SkippableFact]
    public async Task Une_version_perimee_n_ecrase_pas_la_piece()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator());
        await BillingPersistenceScenarios.StaleVersionAsync(harness, DatabaseProvider.PostgreSql, Database("version"));
    }

    [SkippableFact]
    public async Task Une_base_d_un_autre_dossier_est_refusee()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator());
        await TenancyScenarios.StampGuardAsync(harness, DatabaseProvider.PostgreSql, Database("tampon"));
    }

    [SkippableFact]
    public async Task Un_schema_par_dossier_dans_une_meme_base()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator());
        await TenancyScenarios.SchemaPerDossierAsync(harness, DatabaseProvider.PostgreSql, Database("schemas"));
    }

    [SkippableFact]
    public async Task Creation_de_dossier_et_vagues_de_migration_par_le_catalogue()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        string server = new Npgsql.NpgsqlConnectionStringBuilder(_container!.GetConnectionString()) { Database = null }.ConnectionString;
        await using CatalogHarness harness = new(
            s => s.AddPostgreSqlProvider(),
            b => PostgreSqlConfigurator.ConfigureCatalog(b, Database("catalog")),
            new Dictionary<string, string?> { ["Provider"] = "PostgreSql", ["ConnectionString"] = server });
        await CatalogScenarios.ProvisionAndMigrateAsync(harness, DatabaseProvider.PostgreSql);
    }

    [SkippableFact]
    public async Task Registre_des_droits_dans_le_catalogue()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        string server = new Npgsql.NpgsqlConnectionStringBuilder(_container!.GetConnectionString()) { Database = null }.ConnectionString;
        await using CatalogHarness harness = new(
            s => s.AddPostgreSqlProvider(),
            b => PostgreSqlConfigurator.ConfigureCatalog(b, Database("catalog_droits")),
            new Dictionary<string, string?> { ["Provider"] = "PostgreSql", ["ConnectionString"] = server });
        await CatalogScenarios.AuthorizationDirectoryAsync(harness);
    }

    [SkippableFact]
    public async Task Politiques_du_cabinet_dans_le_catalogue()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        string server = new Npgsql.NpgsqlConnectionStringBuilder(_container!.GetConnectionString()) { Database = null }.ConnectionString;
        await using CatalogHarness harness = new(
            s => s.AddPostgreSqlProvider(),
            b => PostgreSqlConfigurator.ConfigureCatalog(b, Database("catalog_politiques")),
            new Dictionary<string, string?> { ["Provider"] = "PostgreSql", ["ConnectionString"] = server });
        await CatalogScenarios.OrganisationPoliciesAsync(harness);
    }
}

public sealed class SqlServerTests : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public async Task InitializeAsync()
    {
        if (Docker.Available)
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
            await _container.StartAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private string Database(string name) =>
        new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_container!.GetConnectionString()) { InitialCatalog = $"dossier_{name}" }.ConnectionString;

    [SkippableFact]
    public async Task Journal_de_synchronisation_reception_outbox_et_reponses_gardees()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator(), syncJournal: true);
        await SyncPersistenceScenarios.JournalFeedOutboxAndReceiptsAsync(harness, DatabaseProvider.SqlServer, Database("sync"));
    }

    [SkippableFact]
    public async Task Outbox_ecrite_avec_le_changement_metier_et_envoyee_aussitot()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        FakeRecipient recipient = new();
        await using TenantHarness harness = OutboxScenarios.Harness(new SqlServerConfigurator(), new ManualClock(OutboxScenarios.Start), recipient);
        await OutboxScenarios.SameTransactionAsync(harness, recipient, DatabaseProvider.SqlServer, Database("outbox"));
    }

    [SkippableFact]
    public async Task Outbox_reprises_espacees_puis_mise_de_cote()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        FakeRecipient recipient = new();
        ManualClock clock = new(OutboxScenarios.Start);
        await using TenantHarness harness = OutboxScenarios.Harness(new SqlServerConfigurator(), clock, recipient);
        await OutboxScenarios.RetryBackoffAndParkingAsync(harness, clock, recipient, DatabaseProvider.SqlServer, Database("outbox-reprise"));
    }

    [SkippableFact]
    public async Task Outbox_reprise_apres_un_arret_en_plein_envoi()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        FakeRecipient recipient = new();
        ManualClock clock = new(OutboxScenarios.Start);
        await using TenantHarness harness = OutboxScenarios.Harness(new SqlServerConfigurator(), clock, recipient);
        await OutboxScenarios.CrashRecoveryAsync(harness, clock, recipient, DatabaseProvider.SqlServer, Database("outbox-arret"));
    }

    [SkippableFact]
    public async Task Outbox_envoyee_par_le_Worker_seul()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        FakeRecipient recipient = new();
        await using TenantHarness harness = OutboxScenarios.Harness(new SqlServerConfigurator(), new ManualClock(OutboxScenarios.Start), recipient, inline: false);
        await OutboxScenarios.WorkerOnlyAsync(harness, recipient, DatabaseProvider.SqlServer, Database("outbox-a"), Database("outbox-b"));
    }

    [SkippableFact]
    public async Task Series_de_poste_ouvertes_recues_dans_l_ordre_et_installees_sur_le_poste()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator());
        await SyncPersistenceScenarios.DeviceSeriesAsync(harness, DatabaseProvider.SqlServer, Database("serie"), Database("serie-poste"));
    }

    [SkippableFact]
    public async Task Validations_concurrentes_dans_la_chaine()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator(), syntheticDocuments: true);
        await IntegrityScenarios.ConcurrentValidationsAsync(harness, DatabaseProvider.SqlServer, Database("chaine"));
    }

    [SkippableFact]
    public async Task Une_ligne_modifiee_en_base_est_detectee()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator(), syntheticDocuments: true);
        await IntegrityScenarios.TamperDetectedAsync(harness, DatabaseProvider.SqlServer, Database("alteration"));
    }

    [SkippableFact]
    public async Task Clotures_signees_et_archive_scellee()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        ManualClock clock = new(DateTimeOffset.UtcNow);
        await using TenantHarness harness = new(new SqlServerConfigurator(), clock, syntheticDocuments: true);
        await IntegrityScenarios.ClosingsAndSealedArchiveAsync(harness, clock, DatabaseProvider.SqlServer, Database("clotures"));
    }

    [SkippableFact]
    public async Task Rotation_des_cles_et_historique_verifiable()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        ManualClock clock = new(DateTimeOffset.UtcNow);
        await using TenantHarness harness = new(new SqlServerConfigurator(), clock, syntheticDocuments: true);
        await IntegrityScenarios.KeyRotationAsync(harness, clock, DatabaseProvider.SqlServer, Database("rotation"));
    }

    [SkippableFact]
    public async Task Aller_retour_d_un_client()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator());
        await CustomerPersistenceScenarios.RoundTripAsync(harness, DatabaseProvider.SqlServer, Database("a"));
    }

    [SkippableFact]
    public async Task Une_base_par_dossier()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator());
        await CustomerPersistenceScenarios.IsolationAsync(harness, DatabaseProvider.SqlServer, Database("b"), Database("c"));
    }

    [SkippableFact]
    public async Task Aller_retour_d_un_fournisseur()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator());
        await PartiesAndCatalogPersistenceScenarios.SupplierRoundTripAsync(harness, DatabaseProvider.SqlServer, Database("f"));
    }

    [SkippableFact]
    public async Task Aller_retour_d_un_article_du_catalogue()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator());
        await PartiesAndCatalogPersistenceScenarios.CatalogRoundTripAsync(harness, DatabaseProvider.SqlServer, Database("g"));
    }

    [SkippableFact]
    public async Task Concurrence_optimiste_sur_le_catalogue()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator());
        await PartiesAndCatalogPersistenceScenarios.OptimisticConcurrencyAsync(harness, DatabaseProvider.SqlServer, Database("conc"));
    }

    [SkippableFact]
    public async Task Recherche_et_unicite_du_catalogue()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator());
        await PartiesAndCatalogPersistenceScenarios.CatalogSearchAndUniquenessAsync(harness, DatabaseProvider.SqlServer, Database("h"));
    }

    [SkippableFact]
    public async Task Recherche_sans_accents_sur_les_tiers_et_le_catalogue()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator());
        await AccentInsensitiveSearchScenarios.SearchIgnoresAccentsAndCaseAsync(harness, DatabaseProvider.SqlServer, Database("rsa"));
    }

    [SkippableFact]
    public async Task Le_remplissage_de_la_migration_donne_la_meme_recherche()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator());
        await AccentInsensitiveSearchScenarios.BackfillMatchesTheCSharpFormAsync(harness, DatabaseProvider.SqlServer, Database("rsb"));
    }

    [SkippableFact]
    public async Task Numerotation_continue_sous_concurrence()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator());
        await TenancyScenarios.NumberingAsync(harness, DatabaseProvider.SqlServer, Database("num"));
    }

    [SkippableFact]
    public async Task Cycle_de_vie_devis_acompte_facture_finale_et_avoir()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator());
        await BillingPersistenceScenarios.LifecycleAsync(harness, DatabaseProvider.SqlServer, Database("devis"));
    }

    [SkippableFact]
    public async Task Fec_des_pieces_validees()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator());
        await ExportScenarios.FecOfValidatedInvoicesAsync(harness, DatabaseProvider.SqlServer, Database("fec"));
    }

    [SkippableFact]
    public async Task Export_fec_en_masse_avec_reprise()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator());
        await ExportScenarios.BulkExportAsync(harness, DatabaseProvider.SqlServer, Database("fec_a"), Database("fec_b"));
    }

    [SkippableFact]
    public async Task Transmission_a_la_plateforme_agreee_simulee()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator(), simulatedPlatform: true);
        await PlatformScenarios.TransmissionAsync(harness, DatabaseProvider.SqlServer, Database("pa"));
    }

    [SkippableFact]
    public async Task Validations_concurrentes_sans_trou_ni_doublon()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator());
        await BillingPersistenceScenarios.ConcurrentValidationsAsync(harness, DatabaseProvider.SqlServer, Database("valid"));
    }

    [SkippableFact]
    public async Task Une_version_perimee_n_ecrase_pas_la_piece()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator());
        await BillingPersistenceScenarios.StaleVersionAsync(harness, DatabaseProvider.SqlServer, Database("version"));
    }

    [SkippableFact]
    public async Task Une_base_d_un_autre_dossier_est_refusee()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator());
        await TenancyScenarios.StampGuardAsync(harness, DatabaseProvider.SqlServer, Database("tampon"));
    }

    [SkippableFact]
    public async Task Creation_de_dossier_et_vagues_de_migration_par_le_catalogue()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        Microsoft.Data.SqlClient.SqlConnectionStringBuilder server = new(_container!.GetConnectionString());
        server.Remove("Initial Catalog");
        server.Remove("Database");
        await using CatalogHarness harness = new(
            s => s.AddSqlServerProvider(),
            b => SqlServerConfigurator.ConfigureCatalog(b, Database("catalog")),
            new Dictionary<string, string?> { ["Provider"] = "SqlServer", ["ConnectionString"] = server.ConnectionString });
        await CatalogScenarios.ProvisionAndMigrateAsync(harness, DatabaseProvider.SqlServer);
    }

    [SkippableFact]
    public async Task Registre_des_droits_dans_le_catalogue()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        Microsoft.Data.SqlClient.SqlConnectionStringBuilder server = new(_container!.GetConnectionString());
        server.Remove("Initial Catalog");
        server.Remove("Database");
        await using CatalogHarness harness = new(
            s => s.AddSqlServerProvider(),
            b => SqlServerConfigurator.ConfigureCatalog(b, Database("catalog_droits")),
            new Dictionary<string, string?> { ["Provider"] = "SqlServer", ["ConnectionString"] = server.ConnectionString });
        await CatalogScenarios.AuthorizationDirectoryAsync(harness);
    }

    [SkippableFact]
    public async Task Politiques_du_cabinet_dans_le_catalogue()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        Microsoft.Data.SqlClient.SqlConnectionStringBuilder server = new(_container!.GetConnectionString());
        server.Remove("Initial Catalog");
        server.Remove("Database");
        await using CatalogHarness harness = new(
            s => s.AddSqlServerProvider(),
            b => SqlServerConfigurator.ConfigureCatalog(b, Database("catalog_politiques")),
            new Dictionary<string, string?> { ["Provider"] = "SqlServer", ["ConnectionString"] = server.ConnectionString });
        await CatalogScenarios.OrganisationPoliciesAsync(harness);
    }
}
