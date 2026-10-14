using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Catalog;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lettra.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Configure EF Core pour Sqlite ; les migrations de ce provider (dossiers et catalogue) vivent dans cet assembly.
/// Le moteur est SQLite3 Multiple Ciphers : une base ouverte avec <c>Password=</c> est chiffrée
/// (ChaCha20-Poly1305 par défaut), une base sans mot de passe reste en clair.
/// </summary>
public sealed class SqliteConfigurator : IDbProviderConfigurator
{
    public static readonly string MigrationsAssembly = typeof(SqliteConfigurator).Assembly.GetName().Name!;

    public SqliteConfigurator() => SqliteEncryption.EnsureInitialized();

    public DatabaseProvider Provider => DatabaseProvider.Sqlite;

    public string ProbeConnectionString => "Data Source=:memory:";

    public void Configure(DbContextOptionsBuilder builder, string connectionString, TenantDescriptor tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        builder.UseSqlite(connectionString, o => o.MigrationsAssembly(MigrationsAssembly));
    }

    public static void ConfigureCatalog(DbContextOptionsBuilder builder, string connectionString)
    {
        SqliteEncryption.EnsureInitialized();
        builder.UseSqlite(connectionString, o => o.MigrationsAssembly(MigrationsAssembly));
    }
}

/// <summary>Branche SQLite3 Multiple Ciphers comme moteur SQLite du processus (une fois).</summary>
public static class SqliteEncryption
{
    private static int _initialized;

    public static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
        {
            SQLitePCL.Batteries_V2.Init();
        }
    }

    /// <summary>Algorithme de chiffrement de la base (vide si le moteur n'est pas SQLite3MC).</summary>
    public static string CipherOf(string connectionString)
    {
        EnsureInitialized();
        using SqliteConnection connection = new(connectionString);
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA cipher;";
        return command.ExecuteScalar()?.ToString() ?? string.Empty;
    }
}

/// <summary>Un dossier SQLite est un fichier : le créer, c'est créer son répertoire ; le supprimer, effacer ses fichiers.</summary>
public sealed class SqliteDatabaseAdministrator : IDatabaseAdministrator
{
    public DatabaseProvider Provider => DatabaseProvider.Sqlite;

    public Task CreateDatabaseAsync(string adminConnectionString, CancellationToken cancellationToken)
    {
        string path = new SqliteConnectionStringBuilder(adminConnectionString).DataSource;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // Le fichier naît à la première ouverture, chiffré avec la clé fournie.
        return Task.CompletedTask;
    }

    public Task DropDatabaseAsync(string adminConnectionString, CancellationToken cancellationToken)
    {
        string path = new SqliteConnectionStringBuilder(adminConnectionString).DataSource;
        SqliteConnection.ClearAllPools();
        foreach (string file in new[] { path, path + "-wal", path + "-shm" })
        {
            File.Delete(file);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Utilisé uniquement par <c>dotnet ef migrations add</c>.</summary>
internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<LettraDbContext>
{
    public LettraDbContext CreateDbContext(string[] args)
    {
        TenantDescriptor design = new(default, default, DatabaseProvider.Sqlite, "design");
        DbContextOptionsBuilder<LettraDbContext> builder = new();
        new SqliteConfigurator().Configure(builder, "Data Source=design.db", design);
        return new LettraDbContext(builder.Options, design);
    }
}

/// <summary>Utilisé uniquement par <c>dotnet ef migrations add --context CatalogDbContext</c>.</summary>
internal sealed class CatalogDesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<CatalogDbContext> builder = new();
        SqliteConfigurator.ConfigureCatalog(builder, "Data Source=catalog-design.db");
        return new CatalogDbContext(builder.Options);
    }
}
