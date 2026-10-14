using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lettra.Infrastructure.Persistence.Sqlite;

/// Modes 1 et 4 : un fichier SQLite chiffré par dossier (SQLite3 Multiple Ciphers, ChaCha20-Poly1305 par défaut).
/// La clé (DEK du dossier) arrive dans la chaîne de connexion via <c>Password=</c> ; elle n'est jamais écrite sur disque en clair.
public sealed class SqliteConfigurator : IDbProviderConfigurator
{
    static SqliteConfigurator() => SqliteEncryption.EnsureInitialized();

    public DatabaseProvider Provider => DatabaseProvider.Sqlite;

    public void Configure(DbContextOptionsBuilder builder, string connectionString, TenantDescriptor tenant) =>
        builder.UseSqlite(connectionString, o => o.MigrationsAssembly(typeof(SqliteConfigurator).Assembly.GetName().Name));
}

public static class SqliteEncryption
{
    private static int _initialized;

    /// Branche le moteur SQLite3MC à la place du SQLite non chiffré.
    public static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
            SQLitePCL.Batteries_V2.Init();
    }

    public static string CipherName(string connectionString)
    {
        EnsureInitialized();
        using var c = new SqliteConnection(connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA cipher;";
        return cmd.ExecuteScalar()?.ToString() ?? "";
    }
}

public sealed class SqliteDatabaseAdministrator : IDatabaseAdministrator
{
    public DatabaseProvider Provider => DatabaseProvider.Sqlite;

    public Task CreateDatabaseAsync(string connectionString, CancellationToken ct = default)
    {
        var path = new SqliteConnectionStringBuilder(connectionString).DataSource;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        return Task.CompletedTask;   // le fichier est créé à la première ouverture, chiffré avec la clé fournie
    }

    public Task DropDatabaseAsync(string connectionString, CancellationToken ct = default)
    {
        var path = new SqliteConnectionStringBuilder(connectionString).DataSource;
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { path, path + "-wal", path + "-shm" })
            File.Delete(f);
        return Task.CompletedTask;
    }
}

/// Utilisé uniquement par <c>dotnet ef migrations add</c>.
public sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<LettraDbContext>
{
    public LettraDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LettraDbContext>();
        new SqliteConfigurator().Configure(options, "Data Source=design.db", DesignTime.Tenant);
        return new LettraDbContext(options.Options, DesignTime.Tenant);
    }
}

internal static class DesignTime
{
    public static readonly TenantDescriptor Tenant = new(Guid.Empty, Guid.Empty, DatabaseProvider.Sqlite, "design");
}
