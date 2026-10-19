using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Mapping;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;
using Lettra.Domain.Catalog;
using Lettra.Domain.Parties;
using Lettra.Infrastructure.Persistence.Search;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Infrastructure.Persistence.Repositories;

internal sealed class SupplierRepository(TenantDbSession session) : ISupplierRepository
{
    public async Task AddAsync(Supplier supplier, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<Supplier>().AddAsync(supplier, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Supplier?> FindAsync(SupplierId id, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<Supplier>().AsTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class SupplierReadStore(TenantDbSession session) : ISupplierReadStore
{
    public async Task<PagedResponse<SupplierSummary>> ListAsync(PartyListFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<Supplier> query = db.Set<Supplier>().AsNoTracking().Where(s => !s.IsArchived).WhereSearchKeyContains(filter.Search);
        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        IOrderedQueryable<Supplier> ordered = filter.Sort == PartySortDto.City
            ? query.By(s => s.Address.City, filter.Direction).ThenBy(s => s.Name)
            : query.By(s => s.Name, filter.Direction);
        var rows = await ordered
            .ThenBy(s => s.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(s => new { s.Id, s.Name, s.Siren, s.Address.City })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<SupplierSummary> items = rows.Select(r => new SupplierSummary(r.Id.Value, r.Name, r.Siren?.Value, r.City)).ToList();
        return new PagedResponse<SupplierSummary>(items, filter.Page, filter.PageSize, total);
    }

    public async Task<SupplierResponse?> GetAsync(SupplierId id, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        Supplier? supplier = await db.Set<Supplier>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false);
        return supplier?.ToResponse();
    }
}

internal sealed class CatalogItemRepository(TenantDbSession session) : ICatalogItemRepository
{
    public async Task AddAsync(CatalogItem item, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<CatalogItem>().AddAsync(item, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CatalogItem?> FindAsync(CatalogItemId id, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<CatalogItem>().AsTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ReferenceExistsAsync(string referenceKey, CatalogItemId? except, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<CatalogItem> query = db.Set<CatalogItem>().AsNoTracking().Where(i => i.ReferenceKey == referenceKey);
        if (except is CatalogItemId excluded)
        {
            query = query.Where(i => i.Id != excluded);
        }

        return await query.AnyAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class CatalogItemReadStore(TenantDbSession session) : ICatalogItemReadStore
{
    public async Task<PagedResponse<CatalogItemSummary>> ListAsync(CatalogItemFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<CatalogItem> query = db.Set<CatalogItem>().AsNoTracking();
        if (!filter.IncludeArchived)
        {
            query = query.Where(i => !i.IsArchived);
        }

        if (filter.Search is { Length: > 0 } search)
        {
            // La désignation est cherchée sur sa forme normalisée (sans accents, en majuscules), identique sur les
            // trois providers ; ReferenceKey est déjà en majuscules.
            string reference = search.ToUpperInvariant();
            string designation = SearchText.Normalize(search);
            query = query.Where(i => i.ReferenceKey.Contains(reference) || EF.Property<string>(i, SearchKey.PropertyName).Contains(designation));
        }

        if (filter.Kind is { } kind)
        {
            query = query.Where(i => i.Kind == kind);
        }

        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        // Désignation comparée sur sa forme normalisée, prix en réel (SQLite ne trie pas les decimal) : même ordre sur
        // les trois providers ; la référence, unique, départage.
        IOrderedQueryable<CatalogItem> ordered = filter.Sort switch
        {
            CatalogSortDto.Designation => query.By(i => EF.Property<string>(i, SearchKey.PropertyName), filter.Direction).ThenBy(i => i.ReferenceKey),
            CatalogSortDto.UnitPrice => query.By(i => (double)i.UnitPriceExclTax, filter.Direction).ThenBy(i => i.ReferenceKey),
            _ => query.By(i => i.ReferenceKey, filter.Direction),
        };
        List<CatalogItem> rows = await ordered
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResponse<CatalogItemSummary>(rows.Select(r => r.ToSummary()).ToList(), filter.Page, filter.PageSize, total);
    }

    public async Task<CatalogItemResponse?> GetAsync(CatalogItemId id, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        CatalogItem? item = await db.Set<CatalogItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken).ConfigureAwait(false);
        return item?.ToResponse();
    }
}

internal static class PartiesAndCatalogRegistration
{
    /// <summary>Dépôts des modules Tiers (fournisseurs) et Catalogue.</summary>
    public static IServiceCollection AddPartiesAndCatalogStores(this IServiceCollection services)
    {
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<ISupplierReadStore, SupplierReadStore>();
        services.AddScoped<ICatalogItemRepository, CatalogItemRepository>();
        services.AddScoped<ICatalogItemReadStore, CatalogItemReadStore>();
        return services;
    }
}
