using Lettra.Application;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>Monte la persistance réelle avec un catalogue en mémoire : un dossier = une base.</summary>
internal sealed class TenantHarness : IAsyncDisposable
{
    private readonly ServiceProvider _root;
    private readonly Dictionary<DossierId, (TenantDescriptor Descriptor, string ConnectionString)> _dossiers = [];

    public TenantHarness(IDbProviderConfigurator configurator)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddApplication();
        services.AddPersistenceCore();
        services.AddSingleton(configurator);
        services.AddSingleton<ITenantCatalog>(new Catalog(_dossiers));
        services.AddSingleton<ISecretStore>(new Secrets(_dossiers));
        services.AddSingleton<ICurrentUser, User>();
        services.AddSingleton<IPermissionService, AllPermissions>();
        services.AddScoped<DossierSource>();
        services.AddScoped<IRequestedDossierSource>(sp => sp.GetRequiredService<DossierSource>());
        _root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public DossierId AddDossier(DatabaseProvider provider, string connectionString)
    {
        DossierId id = new(Guid.CreateVersion7());
        _dossiers[id] = (new TenantDescriptor(id, new OrganisationId(Guid.CreateVersion7()), provider, $"test:{id}"), connectionString);
        return id;
    }

    /// <summary>Ouvre un scope « requête » sur un dossier, comme le fait le filtre de l'API.</summary>
    public async Task<AsyncServiceScope> OpenAsync(DossierId dossier)
    {
        AsyncServiceScope scope = _root.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<DossierSource>().RequestedDossier = dossier;
        Domain.Common.Result result = await scope.ServiceProvider.GetRequiredService<ITenantInitializer>().InitializeAsync(default);
        result.IsSuccess.Should().BeTrue();
        return scope;
    }

    public ValueTask DisposeAsync() => _root.DisposeAsync();

    private sealed class DossierSource : IRequestedDossierSource
    {
        public DossierId? RequestedDossier { get; set; }
    }

    private sealed class Catalog(Dictionary<DossierId, (TenantDescriptor Descriptor, string ConnectionString)> dossiers) : ITenantCatalog
    {
        public ValueTask<TenantDescriptor?> FindAsync(DossierId dossierId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(dossiers.TryGetValue(dossierId, out var entry) ? entry.Descriptor : null);

        public ValueTask<bool> UserHasAccessAsync(Guid userId, DossierId dossierId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(dossiers.ContainsKey(dossierId));
    }

    private sealed class Secrets(Dictionary<DossierId, (TenantDescriptor Descriptor, string ConnectionString)> dossiers) : ISecretStore
    {
        public ValueTask<string> GetConnectionStringAsync(string secretRef, CancellationToken cancellationToken) =>
            ValueTask.FromResult(dossiers.Values.Single(d => d.Descriptor.ConnectionSecretRef == secretRef).ConnectionString);
    }

    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public Guid UserId { get; } = Guid.CreateVersion7();
    }

    private sealed class AllPermissions : IPermissionService
    {
        public ValueTask<IReadOnlySet<Permission>> GetEffectiveAsync(Guid userId, AuthorizationScope scope, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlySet<Permission>>(Enum.GetValues<Permission>().ToHashSet());
    }
}
