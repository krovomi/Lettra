using System.Collections.Concurrent;
using System.Collections.Frozen;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lettra.Infrastructure.Persistence.Tenancy;

/// <summary>Fabrique le <see cref="LettraDbContext"/> du dossier courant, avec le provider et la chaîne de connexion de ce dossier.</summary>
public interface ITenantDbContextFactory
{
    ValueTask<LettraDbContext> CreateAsync(CancellationToken cancellationToken);
}

internal sealed class TenantDbContextFactory(
    ITenantContext tenant,
    ISecretStore secrets,
    IEnumerable<IDbProviderConfigurator> configurators,
    ILoggerFactory loggerFactory) : ITenantDbContextFactory
{
    private readonly FrozenDictionary<DatabaseProvider, IDbProviderConfigurator> _byProvider =
        configurators.ToFrozenDictionary(c => c.Provider);

    public async ValueTask<LettraDbContext> CreateAsync(CancellationToken cancellationToken)
    {
        TenantDescriptor descriptor = tenant.Current;
        if (!_byProvider.TryGetValue(descriptor.Provider, out IDbProviderConfigurator? configurator))
        {
            throw new InvalidOperationException($"Provider {descriptor.Provider} non enregistré dans ce mode de déploiement.");
        }

        string connectionString = await secrets.GetConnectionStringAsync(descriptor.ConnectionSecretRef, cancellationToken).ConfigureAwait(false);
        DbContextOptionsBuilder<LettraDbContext> builder = new DbContextOptionsBuilder<LettraDbContext>()
            .UseLoggerFactory(loggerFactory);
        configurator.Configure(builder, connectionString, descriptor);
        return new LettraDbContext(builder.Options, descriptor);
    }
}

/// <summary>
/// Applique les migrations d'un dossier une seule fois par processus. Suffisant pour le poste isolé et le
/// serveur de cabinet ; en SaaS, <c>Lettra.Worker</c> migrera les bases en amont (phase 2).
/// </summary>
public sealed class TenantMigrator
{
    private readonly ConcurrentDictionary<DossierId, Lazy<Task>> _migrated = new();

    public Task EnsureMigratedAsync(LettraDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        Lazy<Task> migration = _migrated.GetOrAdd(
            context.Tenant.DossierId,
            _ => new Lazy<Task>(() => context.Database.MigrateAsync(CancellationToken.None)));
        return migration.Value.WaitAsync(cancellationToken);
    }
}

/// <summary>Session de base du dossier courant pour la durée du scope : un seul DbContext, créé à la demande.</summary>
internal sealed class TenantDbSession(ITenantDbContextFactory factory, TenantMigrator migrator) : IAsyncDisposable
{
    private LettraDbContext? _context;

    public async ValueTask<LettraDbContext> GetAsync(CancellationToken cancellationToken)
    {
        if (_context is null)
        {
            LettraDbContext context = await factory.CreateAsync(cancellationToken).ConfigureAwait(false);
            await migrator.EnsureMigratedAsync(context, cancellationToken).ConfigureAwait(false);
            _context = context;
        }

        return _context;
    }

    public ValueTask DisposeAsync() => _context?.DisposeAsync() ?? ValueTask.CompletedTask;
}
