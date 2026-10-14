using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.PostgreSql;
using Lettra.Infrastructure.Persistence.Sqlite;
using Lettra.Infrastructure.Persistence.SqlServer;
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

    public void Dispose() => Directory.Delete(_folder, recursive: true);
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
}
