using System.Collections.Concurrent;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Numbering;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Persistence.Tenancy;

public sealed record DossierMigrationResult(DossierId DossierId, string? From, string To, IReadOnlyList<string> Applied);

/// <summary>
/// Migre la base d'un dossier et l'estampille à sa création. Appelé à la création d'un dossier, au démarrage
/// du poste (modes 1 et 4), à la première ouverture en modes 2 et 3, et par <c>Lettra.Worker</c> en SaaS.
/// EF Core 10 pose son propre verrou de migration par base ; le Worker ajoute un bail dans le catalogue.
/// </summary>
public sealed class TenantMigrator(IDossierDbContextFactory factory, TimeProvider clock)
{
    private readonly ConcurrentDictionary<DossierId, Lazy<Task>> _openedOnce = new();

    /// <remarks><c>stampIfMissing</c> :
    /// <c>true</c> à la création du dossier (et pour les bases antérieures à l'estampille en modes 1 et 2, où
    /// le catalogue est local). <c>false</c> dans le Worker : une base sans estampille y est une anomalie.
    /// </remarks>
    public async Task<DossierMigrationResult> MigrateAsync(TenantDescriptor tenant, bool stampIfMissing, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        LettraDbContext context = await factory.CreateAsync(tenant, verifyStamp: false, cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            List<string> applied = [.. await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)];
            List<string> pending = [.. await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)];
            string target = context.Database.GetMigrations().Last();

            // Une base qui porte déjà une estampille doit appartenir au dossier AVANT qu'on la modifie.
            DossierStamp? stamp = applied.Count == 0 ? null : await ReadStampAsync(context, cancellationToken).ConfigureAwait(false);
            if (stamp is not null && !stamp.BelongsTo(tenant))
            {
                throw DossierMismatchException.For(tenant, stamp.DossierId);
            }

            // Une base vierge n'est initialisée qu'à la création du dossier : ailleurs (Worker), c'est une erreur de
            // routage, et on refuse avant d'y créer la moindre table.
            if (applied.Count == 0 && !stampIfMissing)
            {
                throw DossierMismatchException.For(tenant, null);
            }

            if (pending.Count > 0)
            {
                await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }

            if (stamp is null)
            {
                stamp = await ReadStampAsync(context, cancellationToken).ConfigureAwait(false);
                if (stamp is null)
                {
                    if (!stampIfMissing)
                    {
                        throw DossierMismatchException.For(tenant, null);
                    }

                    await StampAsync(context, tenant, cancellationToken).ConfigureAwait(false);
                }
                else if (!stamp.BelongsTo(tenant))
                {
                    throw DossierMismatchException.For(tenant, stamp.DossierId);
                }
            }

            return new DossierMigrationResult(tenant.DossierId, applied.LastOrDefault(), target, pending);
        }
    }

    /// <summary>Modes 1 à 3 : migre et estampille une fois par processus, à la première ouverture du dossier.</summary>
    public Task EnsureMigratedOnceAsync(TenantDescriptor tenant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        Lazy<Task> migration = _openedOnce.GetOrAdd(
            tenant.DossierId,
            _ => new Lazy<Task>(() => MigrateAsync(tenant, stampIfMissing: true, CancellationToken.None)));
        Task task = migration.Value;
        if (task.IsFaulted)
        {
            // Un échec (base momentanément indisponible) ne doit pas rester en cache pour toute la vie du processus.
            _openedOnce.TryRemove(new KeyValuePair<DossierId, Lazy<Task>>(tenant.DossierId, migration));
        }

        return task.WaitAsync(cancellationToken);
    }

    private static Task<DossierStamp?> ReadStampAsync(LettraDbContext context, CancellationToken cancellationToken) =>
        context.DossierStamp.AsNoTracking().SingleOrDefaultAsync(s => s.Id == DossierStamp.SingletonId, cancellationToken);

    private async Task StampAsync(LettraDbContext context, TenantDescriptor tenant, CancellationToken cancellationToken)
    {
        context.DossierStamp.Add(new DossierStamp(tenant, clock.GetUtcNow()));
        foreach ((string code, string prefix) in DefaultSeries)
        {
            if (!await context.NumberSeries.AnyAsync(s => s.Code == code, cancellationToken).ConfigureAwait(false))
            {
                context.NumberSeries.Add(new NumberSeries(code, prefix));
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static readonly (string Code, string Prefix)[] DefaultSeries =
    [
        (NumberSeriesCodes.Invoices, "FAC-"),
        (NumberSeriesCodes.Quotes, "DEV-"),
    ];
}

/// <summary>
/// Session de base du dossier courant pour la durée du scope : un seul DbContext, créé à la demande.
/// En modes 1 à 3, la première ouverture du dossier dans le processus applique les migrations ; en SaaS,
/// c'est le Worker qui migre, et l'API se contente de vérifier l'estampille.
/// </summary>
internal sealed class TenantDbSession(
    ITenantContext tenant,
    ITenantDbContextFactory factory,
    TenantMigrator migrator,
    PersistenceOptions options) : IAsyncDisposable
{
    private LettraDbContext? _context;

    public async ValueTask<LettraDbContext> GetAsync(CancellationToken cancellationToken)
    {
        if (_context is null)
        {
            if (options.MigrateOnOpen)
            {
                await migrator.EnsureMigratedOnceAsync(tenant.Current, cancellationToken).ConfigureAwait(false);
            }

            _context = await factory.CreateAsync(cancellationToken).ConfigureAwait(false);
        }

        return _context;
    }

    public ValueTask DisposeAsync() => _context?.DisposeAsync() ?? ValueTask.CompletedTask;
}

/// <summary>Réglages de persistance choisis par la composition root selon le mode.</summary>
public sealed class PersistenceOptions
{
    /// <summary>Migrer à la première ouverture d'un dossier (modes 1 à 3). <c>false</c> en SaaS : le Worker migre.</summary>
    public bool MigrateOnOpen { get; init; } = true;
}
