using System.Collections.Concurrent;
using System.Collections.Frozen;
using Lettra.Application.Abstractions.Security;
using Lettra.Application.Abstractions.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lettra.Infrastructure.Persistence.Tenancy;

public interface ITenantDbContextFactory
{
    /// Ouvre la base du dossier actif et vérifie qu'elle lui appartient.
    ValueTask<LettraDbContext> CreateAsync(CancellationToken ct = default);
}

/// Fabrique utilisable hors portée de dossier (Worker, provisioning) : le descripteur est passé explicitement.
public interface IDossierDbContextFactory
{
    ValueTask<LettraDbContext> CreateAsync(TenantDescriptor tenant, bool verifyIdentity = true, CancellationToken ct = default);

    /// Dernière migration connue de l'assembly du fournisseur (null si le fournisseur n'est pas enregistré dans ce mode).
    string? LatestMigration(DatabaseProvider provider);
}

public sealed class DossierDbContextFactory(
    ISecretStore secrets,
    IEnumerable<IDbProviderConfigurator> configurators,
    ILoggerFactory loggerFactory) : IDossierDbContextFactory
{
    private readonly FrozenDictionary<DatabaseProvider, IDbProviderConfigurator> _byProvider =
        configurators.ToFrozenDictionary(c => c.Provider);

    private readonly ConcurrentDictionary<DatabaseProvider, string?> _latest = new();

    public string? LatestMigration(DatabaseProvider provider) => _latest.GetOrAdd(provider, p =>
    {
        if (!_byProvider.TryGetValue(p, out var configurator))
            return null;
        // Aucune connexion n'est ouverte : on lit la liste des migrations compilées.
        var probe = new TenantDescriptor(Guid.Empty, Guid.Empty, p, "probe");
        var builder = new DbContextOptionsBuilder<LettraDbContext>();
        configurator.Configure(builder, p switch
        {
            DatabaseProvider.Sqlite => "Data Source=:memory:",
            DatabaseProvider.SqlServer => "Server=probe",
            _ => "Host=probe",
        }, probe);
        using var context = new LettraDbContext(builder.Options, probe);
        return context.Database.GetMigrations().LastOrDefault();
    });

    public async ValueTask<LettraDbContext> CreateAsync(TenantDescriptor tenant, bool verifyIdentity = true, CancellationToken ct = default)
    {
        if (!_byProvider.TryGetValue(tenant.Provider, out var configurator))
            throw new NotSupportedException($"Fournisseur {tenant.Provider} non enregistré pour ce mode de déploiement.");

        var connectionString = await secrets.GetConnectionStringAsync(tenant.ConnectionSecretRef, ct);

        var builder = new DbContextOptionsBuilder<LettraDbContext>()
            .UseLoggerFactory(loggerFactory)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        configurator.Configure(builder, connectionString, tenant);

        var context = new LettraDbContext(builder.Options, tenant);
        if (!verifyIdentity)
            return context;

        try
        {
            await DossierGuard.EnsureBelongsToAsync(context, tenant, ct);
            return context;
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }
}

public sealed class TenantDbContextFactory(ITenantContext tenant, IDossierDbContextFactory factory) : ITenantDbContextFactory
{
    public ValueTask<LettraDbContext> CreateAsync(CancellationToken ct = default) =>
        factory.CreateAsync(tenant.Current, verifyIdentity: true, ct);
}

public static class DossierGuard
{
    /// Une requête de clé primaire sur une table d'une ligne : coût mesuré dans le banc (voir README).
    public static async Task EnsureBelongsToAsync(LettraDbContext context, TenantDescriptor tenant, CancellationToken ct)
    {
        var identity = await context.DossierIdentity
            .Where(x => x.Id == 1)
            .Select(x => new { x.DossierId, x.OrganisationId })
            .SingleOrDefaultAsync(ct);

        if (identity is null || identity.DossierId != tenant.DossierId || identity.OrganisationId != tenant.OrganisationId)
            throw new DossierMismatchException(tenant.DossierId, identity?.DossierId);
    }
}
