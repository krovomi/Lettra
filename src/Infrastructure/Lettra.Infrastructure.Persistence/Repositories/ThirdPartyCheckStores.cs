using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Features.Parties;
using Lettra.Domain.Parties;
using Lettra.Domain.Vigilance;
using Lettra.Infrastructure.Persistence.Concurrency;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Repositories;

/// <summary>
/// La dernière vérification d'un tiers auprès des registres : une ligne par client ou fournisseur vérifié, de même
/// identifiant. Pas de clé étrangère : le tiers est l'un ou l'autre, et la ligne suit la conservation de sa fiche.
/// </summary>
internal sealed class ThirdPartyCheckConfiguration : IEntityTypeConfiguration<ThirdPartyCheck>
{
    public void Configure(EntityTypeBuilder<ThirdPartyCheck> builder)
    {
        builder.ToTable("ThirdPartyChecks");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.FreezeMatch).HasMaxLength(ThirdPartyCheck.MaxNameLength);
        builder.Property(c => c.NoticeLabel).HasMaxLength(ThirdPartyCheck.MaxNameLength);
        builder.Ignore(c => c.NeedsReview);
        builder.IsVersioned();
    }
}

internal sealed class EfThirdPartyCheckStore(TenantDbSession session) : IThirdPartyCheckStore
{
    public async Task<ThirdPartyCheck?> FindAsync(Guid partyId, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<ThirdPartyCheck>().AsTracking().FirstOrDefaultAsync(c => c.Id == partyId, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(ThirdPartyCheck check, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<ThirdPartyCheck>().AddAsync(check, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ThirdPartyEntry>> ListAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        var customers = await db.Set<Customer>().AsNoTracking()
            .Where(c => !c.IsArchived)
            .Select(c => new { c.Id, c.Name, c.Siren })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var suppliers = await db.Set<Supplier>().AsNoTracking()
            .Where(s => !s.IsArchived)
            .Select(s => new { s.Id, s.Name, s.Siren })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<Guid, ThirdPartyCheck> checks = await db.Set<ThirdPartyCheck>().AsNoTracking()
            .ToDictionaryAsync(c => c.Id, cancellationToken).ConfigureAwait(false);
        return
        [
            .. customers.Select(c => new ThirdPartyEntry(ThirdPartyKind.Customer, c.Id.Value, c.Name, c.Siren, checks.GetValueOrDefault(c.Id.Value))),
            .. suppliers.Select(s => new ThirdPartyEntry(ThirdPartyKind.Supplier, s.Id.Value, s.Name, s.Siren, checks.GetValueOrDefault(s.Id.Value))),
        ];
    }
}

/// <summary>Les tiers dont la dernière vérification demande un examen, pour la vue cabinet. Ni nom ni motif.</summary>
internal sealed class ThirdPartiesToReviewIndicatorSource(IThirdPartyCheckStore store) : IDossierIndicatorSource
{
    public async Task<IReadOnlyList<DossierIndicator>> ComputeAsync(DateOnly today, CancellationToken cancellationToken)
    {
        IReadOnlyList<ThirdPartyEntry> entries = await store.ListAsync(cancellationToken).ConfigureAwait(false);
        return ThirdPartyCheckOverview.Indicator([.. entries.Select(e => e.Check).OfType<ThirdPartyCheck>()]) is { } indicator ? [indicator] : [];
    }
}
