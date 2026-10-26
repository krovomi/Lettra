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

/// <summary>Le suivi de l'attestation de vigilance d'un fournisseur : une ligne par fournisseur suivi, de même identifiant.</summary>
internal sealed class SubcontractorAttestationConfiguration : IEntityTypeConfiguration<SubcontractorAttestation>
{
    public void Configure(EntityTypeBuilder<SubcontractorAttestation> builder)
    {
        builder.ToTable("SubcontractorAttestations");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasConversion(id => id.Value, value => new SupplierId(value)).ValueGeneratedNever();
        builder.Property(a => a.ContractAmountExclTax).HasPrecision(18, 2);
        builder.Property(a => a.SecurityCode).HasMaxLength(SubcontractorAttestation.MaxCodeLength);
        builder.Ignore(a => a.IsRequired);
        builder.Ignore(a => a.ValidUntil);
        builder.HasOne<Supplier>().WithOne().HasForeignKey<SubcontractorAttestation>(a => a.Id).OnDelete(DeleteBehavior.Cascade);
        builder.IsVersioned();
    }
}

internal sealed class EfAttestationStore(TenantDbSession session) : IAttestationStore
{
    public async Task<SubcontractorAttestation?> FindAsync(SupplierId supplier, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<SubcontractorAttestation>().AsTracking().FirstOrDefaultAsync(a => a.Id == supplier, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(SubcontractorAttestation attestation, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<SubcontractorAttestation>().AddAsync(attestation, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SupplierAttestationEntry>> ListAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        var suppliers = await db.Set<Supplier>().AsNoTracking()
            .Where(s => !s.IsArchived)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<SupplierId, SubcontractorAttestation> attestations = await db.Set<SubcontractorAttestation>().AsNoTracking()
            .ToDictionaryAsync(a => a.Id, cancellationToken).ConfigureAwait(false);
        return [.. suppliers.Select(s => new SupplierAttestationEntry(s.Id, s.Name, attestations.GetValueOrDefault(s.Id)))];
    }
}

/// <summary>Les fournisseurs à ne pas payer en l'état faute d'attestation de vigilance, pour la vue cabinet. Ni nom ni montant.</summary>
internal sealed class SupplierAttestationsIndicatorSource(IAttestationStore store) : IDossierIndicatorSource
{
    public async Task<IReadOnlyList<DossierIndicator>> ComputeAsync(DateOnly today, CancellationToken cancellationToken)
    {
        IReadOnlyList<SupplierAttestationEntry> entries = await store.ListAsync(cancellationToken).ConfigureAwait(false);
        return AttestationOverview.Indicator([.. entries.Select(e => e.Attestation).OfType<SubcontractorAttestation>()], today) is { } indicator ? [indicator] : [];
    }
}
