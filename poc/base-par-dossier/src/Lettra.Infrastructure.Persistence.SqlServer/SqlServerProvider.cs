using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lettra.Infrastructure.Persistence.SqlServer;

/// Mode 2 (serveur du cabinet sous Windows) : une base SQL Server par dossier.
public sealed class SqlServerConfigurator : IDbProviderConfigurator
{
    public DatabaseProvider Provider => DatabaseProvider.SqlServer;

    public void Configure(DbContextOptionsBuilder builder, string connectionString, TenantDescriptor tenant) =>
        builder.UseSqlServer(connectionString, o =>
        {
            o.MigrationsAssembly(typeof(SqlServerConfigurator).Assembly.GetName().Name);
            o.EnableRetryOnFailure();
            if (tenant.Schema is not null)
                o.MigrationsHistoryTable("__EFMigrationsHistory", tenant.Schema);
        });
}

public sealed class SqlServerDatabaseAdministrator : IDatabaseAdministrator
{
    public DatabaseProvider Provider => DatabaseProvider.SqlServer;

    public async Task CreateDatabaseAsync(string connectionString, CancellationToken ct = default)
    {
        var (master, database) = Split(connectionString);
        await using var c = new SqlConnection(master);
        await c.OpenAsync(ct);
        await using var cmd = new SqlCommand($"IF DB_ID(@name) IS NULL CREATE DATABASE {Quote(database)}", c);
        cmd.Parameters.AddWithValue("@name", database);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DropDatabaseAsync(string connectionString, CancellationToken ct = default)
    {
        var (master, database) = Split(connectionString);
        SqlConnection.ClearAllPools();
        await using var c = new SqlConnection(master);
        await c.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            $"IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE {Quote(database)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {Quote(database)}; END", c);
        cmd.Parameters.AddWithValue("@name", database);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static (string Master, string Database) Split(string connectionString)
    {
        var b = new SqlConnectionStringBuilder(connectionString);
        var database = b.InitialCatalog;
        b.InitialCatalog = "master";
        b.Pooling = false;
        return (b.ConnectionString, database);
    }

    private static string Quote(string identifier) =>
        identifier.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            ? $"[{identifier}]"
            : throw new ArgumentException($"Nom de base invalide : {identifier}");
}

public sealed class SqlServerDesignTimeFactory : IDesignTimeDbContextFactory<LettraDbContext>
{
    public LettraDbContext CreateDbContext(string[] args)
    {
        var tenant = new TenantDescriptor(Guid.Empty, Guid.Empty, DatabaseProvider.SqlServer, "design");
        var options = new DbContextOptionsBuilder<LettraDbContext>();
        new SqlServerConfigurator().Configure(options, "Server=localhost;Database=design;TrustServerCertificate=true", tenant);
        return new LettraDbContext(options.Options, tenant);
    }
}
