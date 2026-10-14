using Lettra.Application.Abstractions.Security;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.PostgreSql;
using Lettra.Infrastructure.Persistence.Sqlite;
using Lettra.Infrastructure.Persistence.Sqlite.Local;
using Lettra.Infrastructure.Persistence.SqlServer;
using Lettra.Infrastructure.Persistence.Tenancy;
using Lettra.Infrastructure.Security;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Lettra.Persistence.Tests;

/// Les trois fournisseurs dans un même conteneur de services, pour exécuter la même suite sur chacun.
/// Connexions : variables LETTRA_TEST_PG et LETTRA_TEST_MSSQL (serveurs locaux par défaut).
/// Un serveur absent fait passer ses tests en « ignoré », jamais en vert.
public sealed class TestEnvironment : IAsyncDisposable
{
    public static readonly string PgAdmin = Environment.GetEnvironmentVariable("LETTRA_TEST_PG")
        ?? "Host=127.0.0.1;Port=5432;Username=postgres;Password=postgres";
    public static readonly string SqlServerAdmin = Environment.GetEnvironmentVariable("LETTRA_TEST_MSSQL")
        ?? "Server=127.0.0.1,1433;User Id=sa;Password=Lettra#Poc2026;TrustServerCertificate=true";

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "lettra-tests", Guid.NewGuid().ToString("N"));
    public ServiceProvider Services { get; }
    public LocalDossierStore LocalStore { get; }

    public TestEnvironment()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lettra:Clusters:pg-test:Provider"] = "PostgreSql",
            ["Lettra:Clusters:pg-test:ConnectionString"] = PgAdmin,
            ["Lettra:Clusters:pg-test:AdminConnectionString"] = PgAdmin,
            ["Lettra:Clusters:mssql-test:Provider"] = "SqlServer",
            ["Lettra:Clusters:mssql-test:ConnectionString"] = SqlServerAdmin,
            ["Lettra:Clusters:mssql-test:AdminConnectionString"] = SqlServerAdmin,
        }).Build();

        LocalStore = new LocalDossierStore(Path.Combine(Root, "dossiers"));
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IKekStore>(new FileKekStore(Path.Combine(Root, "keys")));
        services.AddSingleton<IDossierKeyring, AesGcmDossierKeyring>();
        services.AddSingleton(LocalStore);
        services.AddSingleton<ILocalDossierStore>(LocalStore);
        services.AddSingleton<LocalSqliteSecretStore>();
        services.AddSingleton<ISecretStore>(sp => new CompositeSecretStore(
        [
            (LocalDossierStore.SecretPrefix, sp.GetRequiredService<LocalSqliteSecretStore>()),
            (ClusterSecretStore.AppPrefix, new ClusterSecretStore(config)),
            (ClusterSecretStore.AdminPrefix, new ClusterSecretStore(config)),
        ]));
        services.AddSingleton<IDbProviderConfigurator, SqliteConfigurator>();
        services.AddSingleton<IDbProviderConfigurator, PostgreSqlConfigurator>();
        services.AddSingleton<IDbProviderConfigurator, SqlServerConfigurator>();
        services.AddSingleton<IDatabaseAdministrator, SqliteDatabaseAdministrator>();
        services.AddSingleton<IDatabaseAdministrator, PostgreSqlDatabaseAdministrator>();
        services.AddSingleton<IDatabaseAdministrator, SqlServerDatabaseAdministrator>();
        services.AddSingleton<IDossierDbContextFactory, DossierDbContextFactory>();
        services.AddSingleton<TenantMigrator>();
        services.AddSingleton<LocalDossierProvisioner>();
        Services = services.BuildServiceProvider();
    }

    public IDossierDbContextFactory Factory => Services.GetRequiredService<IDossierDbContextFactory>();
    public TenantMigrator Migrator => Services.GetRequiredService<TenantMigrator>();

    private readonly List<TenantDescriptor> _created = [];

    /// Crée une base vide pour un nouveau dossier (sans la migrer).
    public async Task<TenantDescriptor> NewEmptyDossierAsync(DatabaseProvider provider)
    {
        await EnsureAvailableAsync(provider);
        var org = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();
        TenantDescriptor tenant;
        if (provider == DatabaseProvider.Sqlite)
        {
            var keyring = Services.GetRequiredService<IDossierKeyring>();
            await LocalStore.SaveAsync(new LocalDossierManifest(id, org, "test", LocalStore.DatabasePath(id), await keyring.CreateWrappedDekAsync(org, id)));
            tenant = new TenantDescriptor(id, org, provider, LocalDossierStore.SecretRef(id));
        }
        else
        {
            var cluster = provider == DatabaseProvider.PostgreSql ? "pg-test" : "mssql-test";
            var db = $"t_{id:N}";
            var admin = Services.GetServices<IDatabaseAdministrator>().Single(a => a.Provider == provider);
            var secrets = Services.GetRequiredService<ISecretStore>();
            await admin.CreateDatabaseAsync(await secrets.GetConnectionStringAsync(ClusterSecretStore.AdminRef(cluster, db)));
            tenant = new TenantDescriptor(id, org, provider, ClusterSecretStore.AppRef(cluster, db));
        }
        _created.Add(tenant);
        return tenant;
    }

    public async Task<TenantDescriptor> NewDossierAsync(DatabaseProvider provider)
    {
        var tenant = await NewEmptyDossierAsync(provider);
        await Migrator.MigrateAsync(tenant, stampIdentityIfMissing: true);
        return tenant;
    }

    public ValueTask<LettraDbContext> OpenAsync(TenantDescriptor tenant) => Factory.CreateAsync(tenant);

    private static readonly Dictionary<DatabaseProvider, bool> Availability = [];

    public static async Task EnsureAvailableAsync(DatabaseProvider provider)
    {
        if (provider == DatabaseProvider.Sqlite) return;
        bool ok;
        string reason = "";
        lock (Availability)
            if (Availability.TryGetValue(provider, out ok)) goto done;
        try
        {
            if (provider == DatabaseProvider.PostgreSql)
            {
                await using var c = new NpgsqlConnection(PgAdmin + ";Timeout=3");
                await c.OpenAsync();
            }
            else
            {
                await using var c = new SqlConnection(SqlServerAdmin + ";Connect Timeout=3");
                await c.OpenAsync();
            }
            ok = true;
        }
        catch (Exception ex) { ok = false; reason = ex.Message; }
        lock (Availability) Availability[provider] = ok;
        done:
        Assert.SkipUnless(ok, $"Serveur {provider} indisponible pour ce test. {reason}");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var t in _created.Where(t => t.Provider != DatabaseProvider.Sqlite))
        {
            try
            {
                var admin = Services.GetServices<IDatabaseAdministrator>().Single(a => a.Provider == t.Provider);
                var adminRef = t.ConnectionSecretRef.Replace(ClusterSecretStore.AppPrefix, ClusterSecretStore.AdminPrefix, StringComparison.Ordinal);
                await admin.DropDatabaseAsync(await Services.GetRequiredService<ISecretStore>().GetConnectionStringAsync(adminRef));
            }
            catch { /* nettoyage au mieux */ }
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        await Services.DisposeAsync();
        try { Directory.Delete(Root, recursive: true); } catch { /* idem */ }
    }
}
