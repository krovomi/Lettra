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
    public async Task Recherche_et_unicite_du_catalogue()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        await PartiesAndCatalogPersistenceScenarios.CatalogSearchAndUniquenessAsync(harness, DatabaseProvider.Sqlite, Database("h"));
    }

    [Fact]
    public async Task Numerotation_continue_sous_concurrence()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        await TenancyScenarios.NumberingAsync(harness, DatabaseProvider.Sqlite, Database("num"));
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
        await using TenantHarness harness = new(new SqliteConfigurator());
        await IntegrityScenarios.ConcurrentValidationsAsync(harness, DatabaseProvider.Sqlite, Database("chaine"));
    }

    [Fact]
    public async Task Une_ligne_modifiee_en_base_est_detectee()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        await IntegrityScenarios.TamperDetectedAsync(harness, DatabaseProvider.Sqlite, Database("alteration"));
    }

    [Fact]
    public async Task Clotures_signees_et_archive_scellee()
    {
        ManualClock clock = new(DateTimeOffset.UtcNow);
        await using TenantHarness harness = new(new SqliteConfigurator(), clock);
        await IntegrityScenarios.ClosingsAndSealedArchiveAsync(harness, clock, DatabaseProvider.Sqlite, Database("clotures"));
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
    public async Task Validations_concurrentes_dans_la_chaine()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator());
        await IntegrityScenarios.ConcurrentValidationsAsync(harness, DatabaseProvider.PostgreSql, Database("chaine"));
    }

    [SkippableFact]
    public async Task Une_ligne_modifiee_en_base_est_detectee()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator());
        await IntegrityScenarios.TamperDetectedAsync(harness, DatabaseProvider.PostgreSql, Database("alteration"));
    }

    [SkippableFact]
    public async Task Clotures_signees_et_archive_scellee()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        ManualClock clock = new(DateTimeOffset.UtcNow);
        await using TenantHarness harness = new(new PostgreSqlConfigurator(), clock);
        await IntegrityScenarios.ClosingsAndSealedArchiveAsync(harness, clock, DatabaseProvider.PostgreSql, Database("clotures"));
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
    public async Task Recherche_et_unicite_du_catalogue()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator());
        await PartiesAndCatalogPersistenceScenarios.CatalogSearchAndUniquenessAsync(harness, DatabaseProvider.PostgreSql, Database("h"));
    }

    [SkippableFact]
    public async Task Numerotation_continue_sous_concurrence()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new PostgreSqlConfigurator());
        await TenancyScenarios.NumberingAsync(harness, DatabaseProvider.PostgreSql, Database("num"));
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
    public async Task Validations_concurrentes_dans_la_chaine()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator());
        await IntegrityScenarios.ConcurrentValidationsAsync(harness, DatabaseProvider.SqlServer, Database("chaine"));
    }

    [SkippableFact]
    public async Task Une_ligne_modifiee_en_base_est_detectee()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator());
        await IntegrityScenarios.TamperDetectedAsync(harness, DatabaseProvider.SqlServer, Database("alteration"));
    }

    [SkippableFact]
    public async Task Clotures_signees_et_archive_scellee()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        ManualClock clock = new(DateTimeOffset.UtcNow);
        await using TenantHarness harness = new(new SqlServerConfigurator(), clock);
        await IntegrityScenarios.ClosingsAndSealedArchiveAsync(harness, clock, DatabaseProvider.SqlServer, Database("clotures"));
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
    public async Task Recherche_et_unicite_du_catalogue()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator());
        await PartiesAndCatalogPersistenceScenarios.CatalogSearchAndUniquenessAsync(harness, DatabaseProvider.SqlServer, Database("h"));
    }

    [SkippableFact]
    public async Task Numerotation_continue_sous_concurrence()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using TenantHarness harness = new(new SqlServerConfigurator());
        await TenancyScenarios.NumberingAsync(harness, DatabaseProvider.SqlServer, Database("num"));
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
}
