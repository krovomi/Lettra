using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Persistence.Tenancy;

public sealed record DossierMigrationResult(Guid DossierId, string? From, string To, IReadOnlyList<string> Applied);

/// Migre la base d'un dossier. Au démarrage du poste (modes 1 et 4) ou dans le Worker (modes 2, 3, 5).
/// EF Core 10 pose son propre verrou de migration par base ; le Worker ajoute un bail dans le catalogue
/// pour répartir les bases entre instances et reprendre après une coupure.
public sealed class TenantMigrator(IDossierDbContextFactory factory)
{
    public async Task<DossierMigrationResult> MigrateAsync(TenantDescriptor tenant, bool stampIdentityIfMissing, CancellationToken ct = default)
    {
        await using var context = await factory.CreateAsync(tenant, verifyIdentity: false, ct);

        var applied = (await context.Database.GetAppliedMigrationsAsync(ct)).ToList();
        var pending = (await context.Database.GetPendingMigrationsAsync(ct)).ToList();
        var target = context.Database.GetMigrations().Last();

        // Une base déjà initialisée doit appartenir au dossier avant qu'on la modifie.
        if (applied.Count > 0)
            await DossierGuard.EnsureBelongsToAsync(context, tenant, ct);

        if (pending.Count > 0)
            await context.Database.MigrateAsync(ct);

        if (applied.Count == 0)
        {
            if (!stampIdentityIfMissing)
                throw new DossierMismatchException(tenant.DossierId, null);

            context.DossierIdentity.Add(new DossierIdentity(tenant.DossierId, tenant.OrganisationId));
            context.NumberSeries.Add(new NumberSeries("FAC", "FAC-"));
            await context.SaveChangesAsync(ct);
        }

        return new DossierMigrationResult(tenant.DossierId, applied.LastOrDefault(), target, pending);
    }

    public static string LatestMigration(LettraDbContext context) => context.Database.GetMigrations().Last();
}
