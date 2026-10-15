using Lettra.Application;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Infrastructure;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>Monte la persistance réelle avec un catalogue en mémoire : un dossier = une base.</summary>
internal sealed class TenantHarness : IAsyncDisposable
{
    private readonly ServiceProvider _root;
    private readonly Dictionary<DossierId, (TenantDescriptor Descriptor, string ConnectionString)> _dossiers = [];

    /// <param name="syntheticDocuments">
    /// Vrai pour les scénarios de la chaîne anti-fraude qui journalisent des pièces fictives, sans facture en base : les
    /// pièces ne sont alors pas comparées, comme avant le branchement du module des pièces.
    /// </param>
    public TenantHarness(IDbProviderConfigurator configurator, TimeProvider? clock = null, bool syntheticDocuments = false)
    {
        ServiceCollection services = new();
        services.AddLogging();
        if (clock is not null)
        {
            services.AddSingleton(clock);
        }

        services.AddApplication();
        services.AddPersistenceCore();
        services.AddExports();
        services.AddSingleton(configurator);
        services.AddSingleton<ITenantCatalog>(new Catalog(_dossiers));
        services.AddSingleton<ISecretStore>(new Secrets(_dossiers));
        services.AddSingleton<ICurrentUser, User>();
        services.AddSingleton<IPermissionService, AllPermissions>();
        services.AddSingleton<IKekStore>(Keks);
        services.AddSingleton<IDossierAlertPublisher>(Alerts);
        services.AddScoped<DossierSource>();
        services.AddScoped<IRequestedDossierSource>(sp => sp.GetRequiredService<DossierSource>());
        if (syntheticDocuments)
        {
            services.AddScoped<IJournaledDocumentReader, SyntheticDocuments>();
        }

        _root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public IServiceProvider Services => _root;

    /// <summary>Clés des cabinets (le coffre de l'OS ou de la cellule, en mémoire).</summary>
    public InMemoryKekStore Keks { get; } = new();

    /// <summary>Signaux d'alerte de dossier publiés vers le module Supervision.</summary>
    public CapturedDossierAlerts Alerts { get; } = new();

    public TenantDescriptor Descriptor(DossierId dossier) => _dossiers[dossier].Descriptor;

    public DossierId AddDossier(DatabaseProvider provider, string connectionString, string? schema = null)
    {
        DossierId id = new(Guid.CreateVersion7());
        OrganisationId cabinet = new(Guid.CreateVersion7());
        _dossiers[id] = (new TenantDescriptor(id, cabinet, provider, $"test:{id}", schema), connectionString);
        _ = Keks.GetOrCreateAsync(cabinet, default).AsTask().Result;
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

/// <summary>Ce que la chaîne a publié vers le module Supervision.</summary>
internal sealed class CapturedDossierAlerts : IDossierAlertPublisher
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<DossierAlertSignal> _signals = new();

    public IReadOnlyList<DossierAlertSignal> Signals => [.. _signals];

    public Task PublishAsync(DossierAlertSignal signal, CancellationToken cancellationToken)
    {
        _signals.Enqueue(signal);
        return Task.CompletedTask;
    }
}

/// <summary>Pièces fictives des scénarios de la chaîne : le module des pièces n'est pas interrogé.</summary>
internal sealed class SyntheticDocuments : IJournaledDocumentReader
{
    public bool IsConnected => false;

    public Task<IReadOnlyDictionary<Guid, string>> ComputeContentHashesAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}
