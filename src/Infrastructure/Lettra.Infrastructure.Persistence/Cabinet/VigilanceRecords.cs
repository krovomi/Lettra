using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Features.Cabinet;
using Lettra.Domain.Vigilance;
using Lettra.Infrastructure.Persistence.Concurrency;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Cabinet;

/// <summary>La fiche anti-blanchiment du client, une ligne dans la base du dossier ; ses bénéficiaires effectifs à côté.</summary>
internal sealed class ClientDueDiligenceConfiguration : IEntityTypeConfiguration<ClientDueDiligence>
{
    public void Configure(EntityTypeBuilder<ClientDueDiligence> builder)
    {
        builder.ToTable("ClientDueDiligence");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.Evidence).HasConversion<int>();
        builder.Property(f => f.Level).HasConversion<int>();
        builder.Property(f => f.Factors).HasConversion<int>();
        builder.Property(f => f.Justification).HasMaxLength(ClientDueDiligence.MaxJustificationLength);
        builder.Ignore(f => f.NextReviewOn);
        builder.Ignore(f => f.KeepUntil);
        builder.IsVersioned();

        builder.OwnsMany(f => f.Owners, owner =>
        {
            owner.ToTable("BeneficialOwners");
            owner.WithOwner().HasForeignKey("ClientDueDiligenceId");
            owner.HasKey("ClientDueDiligenceId", nameof(BeneficialOwner.Position));
            owner.Property(o => o.Position).ValueGeneratedNever();
            owner.Property(o => o.Name).HasMaxLength(ClientDueDiligence.MaxNameLength).IsRequired();
            owner.Property(o => o.SharePercent).HasPrecision(5, 2);
            owner.Property(o => o.Kind).HasConversion<int>();
        });
        builder.Navigation(f => f.Owners).HasField("_owners").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class EfVigilanceStore(TenantDbSession session) : IVigilanceStore
{
    public async Task<ClientDueDiligence?> FindAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<ClientDueDiligence>().AsTracking().FirstOrDefaultAsync(f => f.Id == ClientDueDiligence.SingletonId, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(ClientDueDiligence file, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<ClientDueDiligence>().AddAsync(file, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>La fiche anti-blanchiment pour la vue cabinet : absente, incomplète ou à revoir. Ni nom ni niveau de risque.</summary>
internal sealed class ClientDueDiligenceIndicatorSource(TenantDbSession session) : IDossierIndicatorSource
{
    public async Task<IReadOnlyList<DossierIndicator>> ComputeAsync(DateOnly today, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        ClientDueDiligence? file = await db.Set<ClientDueDiligence>().AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == ClientDueDiligence.SingletonId, cancellationToken).ConfigureAwait(false);
        return DueDiligenceOverview.Indicator(file, today) is { } indicator ? [indicator] : [];
    }
}
