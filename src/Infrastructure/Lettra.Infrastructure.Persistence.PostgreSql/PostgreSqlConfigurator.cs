using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Catalog;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Lettra.Infrastructure.Persistence.PostgreSql;

/// <summary>Configure EF Core pour PostgreSql ; les migrations de ce provider (dossiers et catalogue) vivent dans cet assembly.</summary>
public sealed class PostgreSqlConfigurator : IDbProviderConfigurator
{
    public static readonly string MigrationsAssembly = typeof(PostgreSqlConfigurator).Assembly.GetName().Name!;

    public DatabaseProvider Provider => DatabaseProvider.PostgreSql;

    public string ProbeConnectionString => "Host=probe;Database=probe";

    public void Configure(DbContextOptionsBuilder builder, string connectionString, TenantDescriptor tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        builder.UseNpgsql(connectionString, o =>
        {
            o.MigrationsAssembly(MigrationsAssembly);
            o.EnableRetryOnFailure(maxRetryCount: 3);
            if (tenant.Schema is not null)
            {
                o.MigrationsHistoryTable("__EFMigrationsHistory", tenant.Schema);
            }
        });
    }

    public static void ConfigureCatalog(DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseNpgsql(connectionString, o =>
        {
            o.MigrationsAssembly(MigrationsAssembly);
            o.MigrationsHistoryTable("__EFMigrationsHistory", CatalogDbContext.SchemaName);
            o.EnableRetryOnFailure(maxRetryCount: 3);
        });
}

/// <summary>
/// CREATE / DROP DATABASE passent par une connexion d'administration directe au serveur (pas par PgBouncer).
/// </summary>
public sealed class PostgreSqlDatabaseAdministrator : IDatabaseAdministrator
{
    public DatabaseProvider Provider => DatabaseProvider.PostgreSql;

    public async Task CreateDatabaseAsync(string adminConnectionString, CancellationToken cancellationToken)
    {
        (string server, string database) = Split(adminConnectionString);
        NpgsqlConnection connection = new(server);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            NpgsqlCommand exists = new("SELECT 1 FROM pg_database WHERE datname = $1", connection) { Parameters = { new() { Value = database } } };
            await using (exists.ConfigureAwait(false))
            {
                if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
                {
                    return;
                }
            }

            // FILE_COPY : le modèle n'est pas recopié dans le WAL (≈ 7 Mo de WAL économisés par base, mesuré par le POC).
#pragma warning disable CA2100 // Nom de base validé par Quote (lettres, chiffres, souligné).
            NpgsqlCommand create = new($"CREATE DATABASE {Quote(database)} STRATEGY = FILE_COPY", connection);
#pragma warning restore CA2100
            await using (create.ConfigureAwait(false))
            {
                await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task DropDatabaseAsync(string adminConnectionString, CancellationToken cancellationToken)
    {
        (string server, string database) = Split(adminConnectionString);
        NpgsqlConnection.ClearAllPools();
        NpgsqlConnection connection = new(server);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
#pragma warning disable CA2100 // Nom de base validé par Quote.
            NpgsqlCommand drop = new($"DROP DATABASE IF EXISTS {Quote(database)} WITH (FORCE)", connection);
#pragma warning restore CA2100
            await using (drop.ConfigureAwait(false))
            {
                await drop.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static (string Server, string Database) Split(string connectionString)
    {
        NpgsqlConnectionStringBuilder builder = new(connectionString);
        string database = builder.Database ?? throw new ArgumentException("Nom de base manquant.", nameof(connectionString));
        builder.Database = "postgres";
        builder.Pooling = false;
        return (builder.ConnectionString, database);
    }

    private static string Quote(string identifier) =>
        identifier.Length is > 0 and <= 63 && identifier.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            ? $"\"{identifier}\""
            : throw new ArgumentException("Nom de base invalide.", nameof(identifier));
}

/// <summary>Utilisé uniquement par <c>dotnet ef migrations add</c>.</summary>
internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<LettraDbContext>
{
    public LettraDbContext CreateDbContext(string[] args)
    {
        TenantDescriptor design = new(default, default, DatabaseProvider.PostgreSql, "design");
        DbContextOptionsBuilder<LettraDbContext> builder = new();
        new PostgreSqlConfigurator().Configure(builder, "Host=localhost;Database=lettra_design", design);
        return new LettraDbContext(builder.Options, design);
    }
}

/// <summary>Utilisé uniquement par <c>dotnet ef migrations add --context CatalogDbContext</c>.</summary>
internal sealed class CatalogDesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<CatalogDbContext> builder = new();
        PostgreSqlConfigurator.ConfigureCatalog(builder, "Host=localhost;Database=lettra_catalog_design");
        return new CatalogDbContext(builder.Options);
    }
}
