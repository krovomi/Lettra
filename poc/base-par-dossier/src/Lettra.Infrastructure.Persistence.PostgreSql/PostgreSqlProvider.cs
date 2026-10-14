using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Lettra.Infrastructure.Persistence.PostgreSql;

/// Modes 3 et 5 : une base PostgreSQL par dossier, derrière PgBouncer en mode transaction.
public sealed class PostgreSqlConfigurator : IDbProviderConfigurator
{
    public DatabaseProvider Provider => DatabaseProvider.PostgreSql;

    public void Configure(DbContextOptionsBuilder builder, string connectionString, TenantDescriptor tenant) =>
        builder.UseNpgsql(connectionString, o =>
        {
            o.MigrationsAssembly(typeof(PostgreSqlConfigurator).Assembly.GetName().Name);
            o.EnableRetryOnFailure(maxRetryCount: 3);
            if (tenant.Schema is not null)
                o.MigrationsHistoryTable("__EFMigrationsHistory", tenant.Schema);
        });
}

/// CREATE / DROP DATABASE passent par une connexion d'administration directe au cluster (pas par PgBouncer).
public sealed class PostgreSqlDatabaseAdministrator : IDatabaseAdministrator
{
    public DatabaseProvider Provider => DatabaseProvider.PostgreSql;

    public async Task CreateDatabaseAsync(string connectionString, CancellationToken ct = default)
    {
        var (admin, database) = Split(connectionString);
        await using var c = new NpgsqlConnection(admin);
        await c.OpenAsync(ct);
        await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = $1", c) { Parameters = { new() { Value = database } } };
        if (await exists.ExecuteScalarAsync(ct) is not null)
            return;
        // FILE_COPY : pas de copie du modèle dans le WAL (≈ 7 Mo de WAL économisés par base).
        await using var create = new NpgsqlCommand($"CREATE DATABASE {Quote(database)} STRATEGY = FILE_COPY", c);
        await create.ExecuteNonQueryAsync(ct);
    }

    public async Task DropDatabaseAsync(string connectionString, CancellationToken ct = default)
    {
        var (admin, database) = Split(connectionString);
        NpgsqlConnection.ClearAllPools();
        await using var c = new NpgsqlConnection(admin);
        await c.OpenAsync(ct);
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {Quote(database)} WITH (FORCE)", c);
        await drop.ExecuteNonQueryAsync(ct);
    }

    private static (string Admin, string Database) Split(string connectionString)
    {
        var b = new NpgsqlConnectionStringBuilder(connectionString);
        var database = b.Database ?? throw new ArgumentException("Nom de base manquant.", nameof(connectionString));
        b.Database = "postgres";
        b.Pooling = false;
        return (b.ConnectionString, database);
    }

    private static string Quote(string identifier) =>
        identifier.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            ? $"\"{identifier}\""
            : throw new ArgumentException($"Nom de base invalide : {identifier}");
}

public sealed class PostgreSqlDesignTimeFactory : IDesignTimeDbContextFactory<LettraDbContext>
{
    public LettraDbContext CreateDbContext(string[] args)
    {
        var tenant = new TenantDescriptor(Guid.Empty, Guid.Empty, DatabaseProvider.PostgreSql, "design");
        var options = new DbContextOptionsBuilder<LettraDbContext>();
        new PostgreSqlConfigurator().Configure(options, "Host=localhost;Database=design", tenant);
        return new LettraDbContext(options.Options, tenant);
    }
}
