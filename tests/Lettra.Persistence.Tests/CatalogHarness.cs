using System.Security.Cryptography;
using Lettra.Application;
using Lettra.Application.Abstractions.External;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Directory;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Catalog;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Catalog;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Monte la base Catalog réelle (modes 2, 3 et 5) sur un provider : un cabinet, un serveur de bases, la création
/// des bases de dossiers et les vagues de migration.
/// </summary>
internal sealed class CatalogHarness : IAsyncDisposable
{
    public static readonly OrganisationId Organisation = new(new Guid("0199e3a0-0000-7000-8000-0000000000c1"));

    private readonly ServiceProvider _root;

    public CatalogHarness(
        Action<IServiceCollection> addProvider,
        Action<DbContextOptionsBuilder> configureCatalog,
        IDictionary<string, string?> cluster)
    {
        Dictionary<string, string?> settings = new()
        {
            ["Lettra:Organisations:0:Id"] = Organisation.Value.ToString(),
            ["Lettra:Organisations:0:Name"] = "Cabinet de test",
            ["Lettra:Organisations:0:CellId"] = "cellule-test",
            ["Lettra:Organisations:0:Kek"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Lettra:Clusters:srv-a:CellId"] = "cellule-test",
            ["Lettra:Clusters:srv-a:MaxDatabases"] = "100",
        };
        foreach ((string key, string? value) in cluster)
        {
            settings[$"Lettra:Clusters:srv-a:{key}"] = value;
        }

        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddApplication();
        services.AddPersistenceCore();
        addProvider(services);
        services.AddCatalogDatabase(configuration, configureCatalog);
        services.AddSingleton<ICurrentUser, AnyUser>();
        services.AddSingleton<IPermissionService, NoPermissions>();
        services.AddScoped<IRequestedDossierSource, NoRequestedDossier>();
        services.AddSingleton<OfflineDirectories>();
        services.AddSingleton<ICompanyDirectory>(sp => sp.GetRequiredService<OfflineDirectories>());
        services.AddSingleton<IVatNumberChecker>(sp => sp.GetRequiredService<OfflineDirectories>());
        _root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public IServiceProvider Services => _root;

    public async Task<CatalogDossier> DossierAsync(DossierId id)
    {
        await using CatalogDbContext db = await _root.GetRequiredService<IDbContextFactory<CatalogDbContext>>().CreateDbContextAsync();
        return await db.Dossiers.AsNoTracking().SingleAsync(d => d.Id == id.Value);
    }

    public async Task UpdateDossierAsync(DossierId id, Action<CatalogDossier> change)
    {
        await using CatalogDbContext db = await _root.GetRequiredService<IDbContextFactory<CatalogDbContext>>().CreateDbContextAsync();
        CatalogDossier dossier = await db.Dossiers.SingleAsync(d => d.Id == id.Value);
        change(dossier);
        await db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync() => _root.DisposeAsync();

    private sealed class AnyUser : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public Guid UserId { get; } = Guid.CreateVersion7();
    }

    /// <summary>Annuaires externes hors sujet ici : toujours indisponibles.</summary>
    private sealed class OfflineDirectories : ICompanyDirectory, IVatNumberChecker
    {
        public Task<Result<CompanySearchResponse>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken) =>
            Task.FromResult(Result<CompanySearchResponse>.Failure(DirectoryErrors.Unavailable));

        public Task<Result<VatNumberCheckResponse>> CheckAsync(VatNumber vatNumber, CancellationToken cancellationToken) =>
            Task.FromResult(Result<VatNumberCheckResponse>.Failure(DirectoryErrors.Unavailable));
    }

    private sealed class NoPermissions : IPermissionService
    {
        public ValueTask<IReadOnlySet<Permission>> GetEffectiveAsync(Guid userId, DossierId dossier, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlySet<Permission>>(new HashSet<Permission>());
    }
}

/// <summary>Création de dossier, reprise et vagues de migration : joués à l'identique sur chaque provider.</summary>
internal static class CatalogScenarios
{
    public static async Task ProvisionAndMigrateAsync(CatalogHarness harness, DatabaseProvider provider)
    {
        IServiceProvider services = harness.Services;
        await using (CatalogDbContext db = await services.GetRequiredService<IDbContextFactory<CatalogDbContext>>().CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();
        }

        // Plusieurs instances qui démarrent ensemble importent la même configuration sans conflit.
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => services.InitializeCatalogAsync(migrate: false, default))));
        // Puis encore une fois : l'import de la configuration est idempotent.
        await services.InitializeCatalogAsync(migrate: true, default);

        Result<DossierId> created = await Create(services, "Dossier A");
        created.IsSuccess.Should().BeTrue();
        DossierId id = created.Value;

        CatalogDossier dossier = await harness.DossierAsync(id);
        string? latest = services.GetRequiredService<IDossierDbContextFactory>().LatestMigration(provider);
        dossier.State.Should().Be(DossierState.Active);
        dossier.Provider.Should().Be(provider);
        dossier.ClusterId.Should().Be("srv-a");
        dossier.SchemaVersion.Should().Be(latest).And.NotBeNull();
        dossier.WrappedDek.Should().HaveCount(60);
        dossier.ConnectionSecretRef.Should().Be($"cluster:srv-a/d_{id.Value:N}");

        // La base créée porte l'estampille du dossier : elle s'ouvre sous son identité.
        TenantDescriptor descriptor = (await services.GetRequiredService<ITenantCatalog>().FindAsync(id, default))!;
        await using (LettraDbContext opened = await services.GetRequiredService<IDossierDbContextFactory>().CreateAsync(descriptor, verifyStamp: true, default))
        {
            (await opened.Customers.CountAsync()).Should().Be(0);
        }

        (await services.GetRequiredService<IDossierDirectory>().ListAsync(CatalogHarness.Organisation, default))
            .Should().ContainSingle(d => d.Id == id.Value && d.Name == "Dossier A");

        // Une base en retard est reprise par la vague ; une base sous bail d'un autre Worker est laissée.
        DossierId leased = (await Create(services, "Dossier B")).Value;
        await harness.UpdateDossierAsync(id, d => d.SchemaVersion = "20261014000001_InitialCustomers");
        await harness.UpdateDossierAsync(leased, d =>
        {
            d.SchemaVersion = "20261014000001_InitialCustomers";
            d.LeaseOwner = "autre-worker";
            d.LeaseUntil = DateTime.UtcNow.AddMinutes(5);
        });

        DossierMigrationOrchestrator orchestrator = services.GetRequiredService<DossierMigrationOrchestrator>();
        MigrationWaveReport wave = await orchestrator.RunWaveAsync(new MigrationWaveOptions { Owner = "worker-test" }, default);

        wave.Should().BeEquivalentTo(new { Candidates = 2, Migrated = 1, Failed = 0, SkippedLeased = 1, Aborted = false });
        (await harness.DossierAsync(id)).Should().BeEquivalentTo(new { SchemaVersion = latest, LeaseOwner = (string?)null, LastError = (string?)null });

        // Un dossier « en création » (processus arrêté avant la base) est terminé par la reprise.
        await harness.UpdateDossierAsync(leased, d => d.State = DossierState.Provisioning);
        (await services.ResumeDossierProvisioningAsync(default)).Should().Be(1);
        (await harness.DossierAsync(leased)).State.Should().Be(DossierState.Active);
    }

    public static async Task UnknownOrganisationAsync(CatalogHarness harness)
    {
        await harness.Services.InitializeCatalogAsync(migrate: true, default);
        Result<DossierId> result = await harness.Services.GetRequiredService<IDossierProvisioner>()
            .CreateAsync(new OrganisationId(Guid.CreateVersion7()), "X", default);
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(DossierErrors.OrganisationNotFoundCode);
    }

    private static async Task<Result<DossierId>> Create(IServiceProvider services, string name) =>
        await services.GetRequiredService<IDossierProvisioner>().CreateAsync(CatalogHarness.Organisation, name, default);
}

/// <summary>Hors requête : aucun dossier demandé.</summary>
internal sealed class NoRequestedDossier : Lettra.Application.Abstractions.Tenancy.IRequestedDossierSource
{
    public Lettra.Domain.SharedKernel.DossierId? RequestedDossier => null;
}
