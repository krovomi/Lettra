using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Concurrency;
using Lettra.Infrastructure.Persistence.Search;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Repositories;

internal sealed class IbanChangeConfiguration : IEntityTypeConfiguration<IbanChange>
{
    public void Configure(EntityTypeBuilder<IbanChange> builder)
    {
        builder.ToTable("SupplierIbanChanges");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasConversion(id => id.Value, value => new IbanChangeId(value)).ValueGeneratedNever();
        builder.Property(c => c.SupplierId).HasConversion(id => id.Value, value => new SupplierId(value)).IsRequired();
        builder.Property(c => c.PreviousIban).HasMaxLength(20);
        builder.Property(c => c.NewIban).HasMaxLength(20).IsRequired();
        builder.Property(c => c.Status).HasConversion<int>();
        builder.Property(c => c.Method).HasConversion<int?>();
        builder.Property(c => c.Note).HasMaxLength(IbanChange.MaxNoteLength);
        builder.IsVersioned();
        builder.HasIndex(c => new { c.SupplierId, c.Status });
        builder.HasIndex(c => c.Status);
        builder.Ignore(c => c.DomainEvents);
        builder.Ignore(c => c.IsPending);
    }
}

internal sealed class IbanChangeRepository(TenantDbSession session) : IIbanChangeRepository
{
    public async Task AddAsync(IbanChange change, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<IbanChange>().AddAsync(change, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IbanChange?> FindAsync(IbanChangeId id, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<IbanChange>().AsTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IbanChange>> PendingForAsync(SupplierId supplier, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<IbanChange>().AsTracking()
            .Where(c => c.SupplierId == supplier && c.Status == IbanChangeStatus.Pending)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CountPendingAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<IbanChange>().CountAsync(c => c.Status == IbanChangeStatus.Pending, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Les changements avec le nom de leur fournisseur. Tri et pagination en mémoire : quelques lignes par dossier, et SQLite
/// ne trie pas les dates avec fuseau.
/// </summary>
internal sealed class IbanChangeReadStore(TenantDbSession session) : IIbanChangeReadStore
{
    public async Task<PagedResponse<IbanChangeResponse>> ListAsync(IbanChangeListFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<Supplier> suppliers = db.Set<Supplier>().AsNoTracking().WhereSearchKeyContains(filter.Search);
        IQueryable<IbanChange> changes = db.Set<IbanChange>().AsNoTracking();
        if (filter.Status is { } status)
        {
            IbanChangeStatus wanted = (IbanChangeStatus)status;
            changes = changes.Where(c => c.Status == wanted);
        }

        List<IbanChangeResponse> rows = await Join(changes, suppliers, cancellationToken).ConfigureAwait(false);
        IOrderedEnumerable<IbanChangeResponse> ordered = (filter.Sort, filter.Direction) switch
        {
            (IbanChangeSortDto.Supplier, SortDirectionDto.Descending) => rows.OrderByDescending(r => r.SupplierName, StringComparer.CurrentCultureIgnoreCase).ThenByDescending(r => r.ChangedAt),
            (IbanChangeSortDto.Supplier, _) => rows.OrderBy(r => r.SupplierName, StringComparer.CurrentCultureIgnoreCase).ThenByDescending(r => r.ChangedAt),
            (_, SortDirectionDto.Ascending) => rows.OrderBy(r => r.ChangedAt),
            _ => rows.OrderByDescending(r => r.ChangedAt),
        };
        List<IbanChangeResponse> page = [.. ordered.ThenBy(r => r.Id).Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)];
        return new PagedResponse<IbanChangeResponse>(page, filter.Page, filter.PageSize, rows.Count);
    }

    public async Task<IReadOnlyList<IbanChangeResponse>> ForSupplierAsync(SupplierId supplier, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<IbanChangeResponse> rows = await Join(
            db.Set<IbanChange>().AsNoTracking().Where(c => c.SupplierId == supplier), db.Set<Supplier>().AsNoTracking(), cancellationToken).ConfigureAwait(false);
        return [.. rows.OrderByDescending(r => r.ChangedAt).ThenByDescending(r => r.Id)];
    }

    private static async Task<List<IbanChangeResponse>> Join(IQueryable<IbanChange> changes, IQueryable<Supplier> suppliers, CancellationToken cancellationToken)
    {
        var rows = await changes
            .Join(suppliers, c => c.SupplierId, s => s.Id, (c, s) => new { Change = c, s.Name })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(r => ToResponse(r.Change, r.Name))];
    }

    internal static IbanChangeResponse ToResponse(IbanChange c, string supplierName) =>
        new(c.Id.Value, c.SupplierId.Value, supplierName, c.PreviousIban, c.NewIban, c.CountryChanged, c.ChangedAt,
            (IbanChangeStatusDto)c.Status, c.DecidedAt, (IbanVerificationMethodDto?)c.Method, c.Note)
        {
            Version = c.Version,
        };
}

/// <summary>
/// Changements d'IBAN encore à vérifier, pour la vue cabinet : leur nombre et le jour du plus ancien. Critique si l'un
/// d'eux fait changer le compte de pays, à traiter sinon. Rien quand aucun n'attend : la colonne n'apparaît que si besoin.
/// </summary>
internal sealed class IbanChangesIndicatorSource(TenantDbSession session) : IDossierIndicatorSource
{
    public async Task<IReadOnlyList<DossierIndicator>> ComputeAsync(DateOnly today, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        var pending = await db.Set<IbanChange>().AsNoTracking()
            .Where(c => c.Status == IbanChangeStatus.Pending)
            .Select(c => new { c.ChangedAt, c.CountryChanged })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (pending.Count == 0)
        {
            return [];
        }

        IndicatorLevel level = pending.Any(p => p.CountryChanged) ? IndicatorLevel.Critical : IndicatorLevel.Warning;
        DateOnly oldest = LegalCalendar.DateOf(pending.Min(p => p.ChangedAt));
        return [new DossierIndicator(DossierIndicatorKeys.SupplierIbanToVerify, pending.Count, level, oldest)];
    }
}
