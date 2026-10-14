using Lettra.Application.Abstractions.Tenancy;
using Lettra.Composition;
using Lettra.Infrastructure.Catalog;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Lettra.Persistence.Tests;

/// Mode 5 : catalogue, création de dossiers, résolution avec contrôle d'accès, vagues de migration.
public sealed class CatalogTests : IAsyncLifetime
{
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..12];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lettra-tests", Guid.NewGuid().ToString("N"));
    private ServiceProvider _sp = null!;
    private Guid _org;

    private string CatalogDb => $"catalog_{_suffix}";

    public async ValueTask InitializeAsync()
    {
        await TestEnvironment.EnsureAvailableAsync(DatabaseProvider.PostgreSql);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lettra:DeploymentMode"] = "HostedSaaS",
            ["Lettra:Catalog:ConnectionString"] = $"{TestEnvironment.PgAdmin};Database={CatalogDb}",
            ["Lettra:Keys:Directory"] = Path.Combine(_root, "keys"),
            ["Lettra:Clusters:pg-a:Provider"] = "PostgreSql",
            ["Lettra:Clusters:pg-a:ConnectionString"] = TestEnvironment.PgAdmin,
            ["Lettra:Clusters:pg-a:AdminConnectionString"] = TestEnvironment.PgAdmin,
        }).Build();
        _sp = new ServiceCollection().AddLettraPersistence(config).BuildServiceProvider();
        await _sp.MigrateCatalogAsync();

        await using var db = await Catalog().CreateDbContextAsync();
        _org = Guid.CreateVersion7();
        db.Organisations.Add(new Organisation { Id = _org, Name = "Cabinet test", CellId = "cell-1" });
        db.Clusters.Add(new DbCluster { Id = "pg-a", CellId = "cell-1", Provider = DatabaseProvider.PostgreSql, MaxDatabases = 1000 });
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_sp is null) return;
        await using (var db = await Catalog().CreateDbContextAsync())
        {
            var names = await db.Dossiers.Select(d => d.DatabaseName).ToListAsync();
            NpgsqlConnection.ClearAllPools();
            await using var c = new NpgsqlConnection(TestEnvironment.PgAdmin);
            await c.OpenAsync();
            foreach (var n in names.Append(CatalogDb))
                await new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{n}\" WITH (FORCE)", c).ExecuteNonQueryAsync();
        }
        await _sp.DisposeAsync();
        try { Directory.Delete(_root, true); } catch { /* au mieux */ }
    }

    private IDbContextFactory<CatalogDbContext> Catalog() => _sp.GetRequiredService<IDbContextFactory<CatalogDbContext>>();

    private sealed class FakeRequest(string? user, Guid? org, Guid? dossier) : ICurrentRequest
    {
        public string? UserId => user;
        public Guid? OrganisationId => org;
        public Guid? DossierId => dossier;
    }

    private CatalogTenantResolver Resolver(string? user, Guid? org, Guid? dossier) =>
        ActivatorUtilities.CreateInstance<CatalogTenantResolver>(_sp, new FakeRequest(user, org, dossier));

    [Fact]
    public async Task Le_resolveur_ne_donne_un_dossier_qu_a_un_utilisateur_autorise_du_bon_cabinet()
    {
        var ct = TestContext.Current.CancellationToken;
        var provisioner = _sp.GetRequiredService<DossierProvisioner>();
        var d = await provisioner.ProvisionAsync(_org, "SARL Dupont", ct: ct);
        await using (var db = await Catalog().CreateDbContextAsync(ct))
        {
            db.Access.Add(new DossierAccess { UserId = "alice", DossierId = d.Id });
            await db.SaveChangesAsync(ct);
        }

        var tenant = await Resolver("alice", _org, d.Id).ResolveAsync(ct);
        Assert.Equal(d.Id, tenant.DossierId);
        Assert.DoesNotContain("Password", tenant.ConnectionSecretRef, StringComparison.OrdinalIgnoreCase);

        await Assert.ThrowsAsync<TenantAccessDeniedException>(async () => await Resolver("bob", _org, d.Id).ResolveAsync(ct));
        await Assert.ThrowsAsync<TenantAccessDeniedException>(async () => await Resolver("alice", Guid.CreateVersion7(), d.Id).ResolveAsync(ct));
        await Assert.ThrowsAsync<TenantNotFoundException>(async () => await Resolver("alice", _org, null).ResolveAsync(ct));

        // Bout en bout : portée de requête → contexte de dossier → base vérifiée.
        await using var scope = _sp.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant);
        await using var ctx = await scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>().CreateAsync(ct);
        Assert.Equal(d.Id, (await ctx.DossierIdentity.SingleAsync(ct)).DossierId);
    }

    [Fact]
    public async Task Une_vague_de_migration_commence_par_un_canari_puis_reprend_la_ou_elle_s_est_arretee()
    {
        var ct = TestContext.Current.CancellationToken;
        var provisioner = _sp.GetRequiredService<DossierProvisioner>();
        var factory = _sp.GetRequiredService<IDossierDbContextFactory>();
        var dossiers = new List<Dossier>();
        for (var i = 0; i < 12; i++)
            dossiers.Add(await provisioner.ProvisionAsync(_org, $"Dossier {i}", ct: ct));

        // Toutes les bases reviennent à la migration 1, comme avant une mise à jour de l'application.
        foreach (var d in dossiers)
        {
            await using var ctx = await factory.CreateAsync(TenantCatalog.ToDescriptor(d), ct: ct);
            await ctx.GetService<IMigrator>().MigrateAsync("20261014000001_Initial", ct);
        }
        await using (var db = await Catalog().CreateDbContextAsync(ct))
        {
            await db.Dossiers.ExecuteUpdateAsync(s => s.SetProperty(d => d.SchemaVersion, "20261014000001_Initial"), ct);
            // Un autre Worker tient le bail du dernier dossier.
            var last = dossiers[^1].Id;
            await db.Dossiers.Where(d => d.Id == last).ExecuteUpdateAsync(s => s
                .SetProperty(d => d.LeaseOwner, "autre-worker")
                .SetProperty(d => d.LeaseUntil, DateTimeOffset.UtcNow.AddMinutes(5)), ct);
        }

        var orchestrator = _sp.GetRequiredService<DossierMigrationOrchestrator>();
        var canary = await orchestrator.RunWaveAsync(new MigrationWaveOptions { Limit = 2, MaxParallelism = 4 }, ct);
        Assert.Equal((2, 2, 0), (canary.Candidates, canary.Migrated, canary.Failed));

        var rest = await orchestrator.RunWaveAsync(new MigrationWaveOptions { MaxParallelism = 4 }, ct);
        Assert.Equal((10, 9, 1, 0), (rest.Candidates, rest.Migrated, rest.SkippedLeased, rest.Failed));

        var again = await orchestrator.RunWaveAsync(new MigrationWaveOptions(), ct);
        Assert.Equal(1, again.Candidates);   // seul le dossier sous bail étranger reste à faire

        await using (var db = await Catalog().CreateDbContextAsync(ct))
        {
            var versions = await db.Dossiers.GroupBy(d => d.SchemaVersion).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
            Assert.Contains(versions, v => v.Key == "20261014000002_AddInvoiceDueDate" && v.N == 11);
        }
    }

    [Fact]
    public async Task Une_vague_s_arrete_quand_trop_de_bases_echouent()
    {
        var ct = TestContext.Current.CancellationToken;
        var provisioner = _sp.GetRequiredService<DossierProvisioner>();
        for (var i = 0; i < 25; i++)
            await provisioner.ProvisionAsync(_org, $"Dossier {i}", ct: ct);

        // Le catalogue annonce une version ancienne alors que les bases ont disparu : chaque migration échoue.
        await using (var db = await Catalog().CreateDbContextAsync(ct))
        {
            await db.Dossiers.ExecuteUpdateAsync(s => s
                .SetProperty(d => d.SchemaVersion, "20261014000001_Initial")
                .SetProperty(d => d.DatabaseName, d => d.DatabaseName + "_absente"), ct);
        }

        var report = await _sp.GetRequiredService<DossierMigrationOrchestrator>()
            .RunWaveAsync(new MigrationWaveOptions { MaxParallelism = 1, MaxFailureRatio = 0.05 }, ct);
        Assert.True(report.Aborted);
        Assert.Equal(0, report.Migrated);
        Assert.InRange(report.Failed, 20, 22);

        await using (var db = await Catalog().CreateDbContextAsync(ct))
        {
            await db.Dossiers.ExecuteUpdateAsync(s => s.SetProperty(d => d.DatabaseName, d => d.DatabaseName.Replace("_absente", "")), ct);
        }
    }
}
