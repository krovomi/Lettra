using Lettra.Application.Abstractions.Cabinet;
using Lettra.Domain.Missions;
using Lettra.Infrastructure.Persistence.Concurrency;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Cabinet;

/// <summary>La mission du cabinet, une ligne dans la base du dossier, comme la fiche de l'entreprise.</summary>
internal sealed class MissionConfiguration : IEntityTypeConfiguration<Mission>
{
    public void Configure(EntityTypeBuilder<Mission> builder)
    {
        builder.ToTable("Mission");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Kind).HasConversion<int>();
        builder.Property(m => m.Services).HasConversion<int>();
        builder.Property(m => m.Schedule).HasConversion<int>();
        builder.Property(m => m.ClientSignatory).HasMaxLength(Mission.MaxNameLength).IsRequired();
        builder.Property(m => m.Manager).HasMaxLength(Mission.MaxNameLength);
        builder.Ignore(m => m.FeePerInstalment);
        builder.Ignore(m => m.BudgetHours);
        builder.IsVersioned();
    }
}

/// <summary>Les temps passés sur le dossier, lus par période.</summary>
internal sealed class TimeEntryConfiguration : IEntityTypeConfiguration<TimeEntry>
{
    public void Configure(EntityTypeBuilder<TimeEntry> builder)
    {
        builder.ToTable("TimeEntries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Task).HasConversion<int>();
        builder.Property(e => e.Note).HasMaxLength(TimeEntry.MaxNoteLength);
        builder.HasIndex(e => e.Date);
    }
}

internal sealed class EfMissionStore(TenantDbSession session) : IMissionStore
{
    public async Task<Mission?> FindAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<Mission>().AsTracking().FirstOrDefaultAsync(m => m.Id == Mission.SingletonId, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(Mission mission, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<Mission>().AddAsync(mission, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TimeEntry>> ListTimeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<TimeEntry>().AsNoTracking()
            .Where(e => e.Date >= from && e.Date <= to)
            .OrderByDescending(e => e.Date)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TimeEntry?> FindTimeAsync(Guid id, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<TimeEntry>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddTimeAsync(TimeEntry entry, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<TimeEntry>().AddAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveTimeAsync(TimeEntry entry, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        db.Set<TimeEntry>().Remove(entry);
    }
}
