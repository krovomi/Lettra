using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Catalog;
using Lettra.Infrastructure.Persistence.Supervision;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lettra.Infrastructure.Persistence.SqlServer;

/// <summary>
/// Configure EF Core pour SqlServer (serveur du cabinet, mode 2) ; les migrations de ce provider (dossiers et
/// catalogue) vivent dans cet assembly. <c>Microsoft.Data.SqlClient</c> refuse <c>InvariantGlobalization</c>.
/// </summary>
public sealed class SqlServerConfigurator : IDbProviderConfigurator
{
    public static readonly string MigrationsAssembly = typeof(SqlServerConfigurator).Assembly.GetName().Name!;

    public DatabaseProvider Provider => DatabaseProvider.SqlServer;

    public string ProbeConnectionString => "Server=probe;Database=probe";

    public void Configure(DbContextOptionsBuilder builder, string connectionString, TenantDescriptor tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        builder.UseSqlServer(connectionString, o =>
        {
            o.MigrationsAssembly(MigrationsAssembly);
            o.EnableRetryOnFailure();
            if (tenant.Schema is not null)
            {
                o.MigrationsHistoryTable("__EFMigrationsHistory", tenant.Schema);
            }
        });
    }

    /// <summary>Base de supervision : son schéma et son propre historique de migrations, souvent dans la base Catalog.</summary>
    public static void ConfigureSupervision(DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseSqlServer(connectionString, o =>
        {
            o.MigrationsAssembly(MigrationsAssembly);
            o.MigrationsHistoryTable(SupervisionDbContext.MigrationsHistoryTable, SupervisionDbContext.SchemaName);
            o.EnableRetryOnFailure();
        });

    public static void ConfigureCatalog(DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseSqlServer(connectionString, o =>
        {
            o.MigrationsAssembly(MigrationsAssembly);
            o.MigrationsHistoryTable("__EFMigrationsHistory", CatalogDbContext.SchemaName);
            o.EnableRetryOnFailure();
        });
}

public sealed class SqlServerDatabaseAdministrator : IDatabaseAdministrator
{
    public DatabaseProvider Provider => DatabaseProvider.SqlServer;

    public async Task CreateDatabaseAsync(string adminConnectionString, CancellationToken cancellationToken)
    {
        (string master, string database) = Split(adminConnectionString);
        SqlConnection connection = new(master);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
#pragma warning disable CA2100 // Nom de base validé par Quote.
            SqlCommand command = new($"IF DB_ID(@name) IS NULL CREATE DATABASE {Quote(database)}", connection);
#pragma warning restore CA2100
            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("@name", database);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task DropDatabaseAsync(string adminConnectionString, CancellationToken cancellationToken)
    {
        (string master, string database) = Split(adminConnectionString);
        SqlConnection.ClearAllPools();
        SqlConnection connection = new(master);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
#pragma warning disable CA2100 // Nom de base validé par Quote.
            SqlCommand command = new(
                $"IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE {Quote(database)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {Quote(database)}; END",
                connection);
#pragma warning restore CA2100
            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("@name", database);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static (string Master, string Database) Split(string connectionString)
    {
        SqlConnectionStringBuilder builder = new(connectionString);
        string database = builder.InitialCatalog;
        builder.InitialCatalog = "master";
        builder.Pooling = false;
        return (builder.ConnectionString, database);
    }

    private static string Quote(string identifier) =>
        identifier.Length is > 0 and <= 128 && identifier.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            ? $"[{identifier}]"
            : throw new ArgumentException("Nom de base invalide.", nameof(identifier));
}

/// <summary>Utilisé uniquement par <c>dotnet ef migrations add</c>.</summary>
internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<LettraDbContext>
{
    public LettraDbContext CreateDbContext(string[] args)
    {
        TenantDescriptor design = new(default, default, DatabaseProvider.SqlServer, "design");
        DbContextOptionsBuilder<LettraDbContext> builder = new();
        new SqlServerConfigurator().Configure(builder, "Server=localhost;Database=lettra_design;TrustServerCertificate=true", design);
        return new LettraDbContext(builder.Options, design);
    }
}

/// <summary>Utilisé uniquement par <c>dotnet ef migrations add --context CatalogDbContext</c>.</summary>
internal sealed class CatalogDesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<CatalogDbContext> builder = new();
        SqlServerConfigurator.ConfigureCatalog(builder, "Server=localhost;Database=lettra_catalog_design;TrustServerCertificate=true");
        return new CatalogDbContext(builder.Options);
    }
}

/// <summary>Utilisé uniquement par <c>dotnet ef migrations add --context SupervisionDbContext</c>.</summary>
internal sealed class SupervisionDesignTimeFactory : IDesignTimeDbContextFactory<SupervisionDbContext>
{
    public SupervisionDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<SupervisionDbContext> builder = new();
        SqlServerConfigurator.ConfigureSupervision(builder, "Server=localhost;Database=lettra_catalog_design;TrustServerCertificate=true");
        return new SupervisionDbContext(builder.Options);
    }
}
