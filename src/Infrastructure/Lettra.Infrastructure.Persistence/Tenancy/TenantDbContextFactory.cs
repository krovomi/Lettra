using System.Collections.Concurrent;
using System.Collections.Frozen;
using Lettra.Application.Abstractions.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lettra.Infrastructure.Persistence.Tenancy;

/// <summary>Fabrique le <see cref="LettraDbContext"/> du dossier courant, vérifié par son estampille.</summary>
public interface ITenantDbContextFactory
{
    ValueTask<LettraDbContext> CreateAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Fabrique utilisable hors d'une portée de dossier (Worker, création de dossier) : le descripteur est
/// passé explicitement.
/// </summary>
public interface IDossierDbContextFactory
{
    /// <remarks><c>verifyStamp</c> :
    /// <c>false</c> seulement pour le migrateur, qui vérifie lui-même l'estampille d'une base déjà initialisée.
    /// </remarks>
    ValueTask<LettraDbContext> CreateAsync(TenantDescriptor tenant, bool verifyStamp, CancellationToken cancellationToken);

    /// <summary>
    /// Dernière migration compilée pour ce provider, sans ouvrir de connexion. Les trois assemblies de migrations
    /// portent les mêmes identifiants, ce qui permet au Worker de comparer les versions d'un provider à l'autre.
    /// </summary>
    string? LatestMigration(DatabaseProvider provider);
}

internal sealed class DossierDbContextFactory(
    ISecretStore secrets,
    IEnumerable<IDbProviderConfigurator> configurators,
    ILoggerFactory loggerFactory) : IDossierDbContextFactory
{
    private readonly FrozenDictionary<DatabaseProvider, IDbProviderConfigurator> _byProvider =
        configurators.ToFrozenDictionary(c => c.Provider);

    private readonly ConcurrentDictionary<DatabaseProvider, string?> _latest = new();

    public async ValueTask<LettraDbContext> CreateAsync(TenantDescriptor tenant, bool verifyStamp, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        IDbProviderConfigurator configurator = ConfiguratorFor(tenant.Provider);
        string connectionString = await secrets.GetConnectionStringAsync(tenant.ConnectionSecretRef, cancellationToken).ConfigureAwait(false);

        DbContextOptionsBuilder<LettraDbContext> builder = new DbContextOptionsBuilder<LettraDbContext>()
            .UseLoggerFactory(loggerFactory);
        configurator.Configure(builder, connectionString, tenant);
        LettraDbContext context = new(builder.Options, tenant);
        if (!verifyStamp)
        {
            return context;
        }

        try
        {
            await DossierGuard.EnsureBelongsToAsync(context, tenant, cancellationToken).ConfigureAwait(false);
            return context;
        }
        catch
        {
            await context.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public string? LatestMigration(DatabaseProvider provider) => _latest.GetOrAdd(provider, p =>
    {
        if (!_byProvider.TryGetValue(p, out IDbProviderConfigurator? configurator))
        {
            return null;
        }

        TenantDescriptor probe = new(default, default, p, "probe");
        DbContextOptionsBuilder<LettraDbContext> builder = new();
        configurator.Configure(builder, configurator.ProbeConnectionString, probe);
        using LettraDbContext context = new(builder.Options, probe);
        return context.Database.GetMigrations().LastOrDefault();
    });

    private IDbProviderConfigurator ConfiguratorFor(DatabaseProvider provider) =>
        _byProvider.TryGetValue(provider, out IDbProviderConfigurator? configurator)
            ? configurator
            : throw new InvalidOperationException($"Provider {provider} non enregistré dans ce mode de déploiement.");
}

internal sealed class TenantDbContextFactory(ITenantContext tenant, IDossierDbContextFactory factory) : ITenantDbContextFactory
{
    public ValueTask<LettraDbContext> CreateAsync(CancellationToken cancellationToken) =>
        factory.CreateAsync(tenant.Current, verifyStamp: true, cancellationToken);
}
