using Lettra.Application.Abstractions.Cabinet;
using Lettra.Domain.Review;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Cabinet;

/// <summary>Les coches de la revue, dans la base du dossier : elles suivent le dossier (export, sauvegarde, poste isolé).</summary>
internal sealed class DossierReviewMarkConfiguration : IEntityTypeConfiguration<DossierReviewMark>
{
    public void Configure(EntityTypeBuilder<DossierReviewMark> builder)
    {
        builder.ToTable("ReviewMarks");
        builder.HasKey(m => new { m.Period, m.ItemKey });
        builder.Property(m => m.Period).HasMaxLength(7);
        builder.Property(m => m.ItemKey).HasMaxLength(40);
        builder.Property(m => m.Note).HasMaxLength(DossierReviewMark.MaxNoteLength);
    }
}

internal sealed class EfDossierReviewStore(TenantDbSession session) : IDossierReviewStore
{
    public async Task<IReadOnlyList<DossierReviewMark>> ListAsync(ReviewPeriod period, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        string key = period.ToString();
        return await db.Set<DossierReviewMark>().AsNoTracking().Where(m => m.Period == key).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(DossierReviewMark mark, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mark);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        DossierReviewMark? existing = await db.Set<DossierReviewMark>().AsTracking()
            .FirstOrDefaultAsync(m => m.Period == mark.Period && m.ItemKey == mark.ItemKey, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            db.Remove(existing);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        db.Add(mark);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(ReviewPeriod period, string itemKey, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        string key = period.ToString();
        DossierReviewMark? existing = await db.Set<DossierReviewMark>().AsTracking()
            .FirstOrDefaultAsync(m => m.Period == key && m.ItemKey == itemKey, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            db.Remove(existing);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
