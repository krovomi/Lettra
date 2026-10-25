using Lettra.Application.Abstractions.External;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Mapping;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Directory;
using Lettra.Contracts.Suppliers;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Application.Tests;

/// <summary>Faux dépôts des fournisseurs et du catalogue ; un seul « commit » partagé, comme une transaction de dossier.</summary>
internal sealed class InMemoryPartiesAndCatalog : ISupplierRepository, ISupplierReadStore, ICatalogItemRepository, ICatalogItemReadStore
{
    public Dictionary<SupplierId, Supplier> Suppliers { get; } = [];

    public Dictionary<CatalogItemId, CatalogItem> Items { get; } = [];

    public Task AddAsync(Supplier supplier, CancellationToken cancellationToken)
    {
        Suppliers[supplier.Id] = supplier;
        return Task.CompletedTask;
    }

    public Task<Supplier?> FindAsync(SupplierId id, CancellationToken cancellationToken) => Task.FromResult(Suppliers.GetValueOrDefault(id));

    public PartyListFilter? LastPartyFilter { get; private set; }

    public Task<PagedResponse<SupplierSummary>> ListAsync(PartyListFilter filter, CancellationToken cancellationToken)
    {
        LastPartyFilter = filter;
        return Task.FromResult(new PagedResponse<SupplierSummary>(
            Suppliers.Values.Where(s => !s.IsArchived).Select(s => new SupplierSummary(s.Id.Value, s.Name, s.Siren?.Value, s.Address.City)).ToList(),
            filter.Page, filter.PageSize, Suppliers.Values.Count(s => !s.IsArchived)));
    }

    public Task<SupplierResponse?> GetAsync(SupplierId id, CancellationToken cancellationToken) =>
        Task.FromResult(Suppliers.GetValueOrDefault(id)?.ToResponse());

    public Task AddAsync(CatalogItem item, CancellationToken cancellationToken)
    {
        Items[item.Id] = item;
        return Task.CompletedTask;
    }

    public Task<CatalogItem?> FindAsync(CatalogItemId id, CancellationToken cancellationToken) => Task.FromResult(Items.GetValueOrDefault(id));

    public Task<bool> ReferenceExistsAsync(string referenceKey, CatalogItemId? except, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Values.Any(i => i.ReferenceKey == referenceKey && i.Id != except));

    public CatalogItemFilter? LastFilter { get; private set; }

    public Task<PagedResponse<CatalogItemSummary>> ListAsync(CatalogItemFilter filter, CancellationToken cancellationToken)
    {
        LastFilter = filter;
        List<CatalogItemSummary> items = Items.Values.Where(i => filter.IncludeArchived || !i.IsArchived).Select(i => i.ToSummary()).ToList();
        return Task.FromResult(new PagedResponse<CatalogItemSummary>(items, filter.Page, filter.PageSize, items.Count));
    }

    public Task<CatalogItemResponse?> GetAsync(CatalogItemId id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.GetValueOrDefault(id)?.ToResponse());
}

/// <summary>Faux dépôt des changements d'IBAN : la liste lit le nom du fournisseur dans le faux dépôt des fournisseurs.</summary>
internal sealed class InMemoryIbanChanges(InMemoryPartiesAndCatalog parties) : IIbanChangeRepository, IIbanChangeReadStore
{
    public Dictionary<IbanChangeId, IbanChange> Changes { get; } = [];

    public Task AddAsync(IbanChange change, CancellationToken cancellationToken)
    {
        Changes[change.Id] = change;
        return Task.CompletedTask;
    }

    public Task<IbanChange?> FindAsync(IbanChangeId id, CancellationToken cancellationToken) => Task.FromResult(Changes.GetValueOrDefault(id));

    public Task<IReadOnlyList<IbanChange>> PendingForAsync(SupplierId supplier, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<IbanChange>>([.. Changes.Values.Where(c => c.SupplierId == supplier && c.IsPending)]);

    public Task<int> CountPendingAsync(CancellationToken cancellationToken) => Task.FromResult(Changes.Values.Count(c => c.IsPending));

    public IbanChangeListFilter? LastFilter { get; private set; }

    public Task<PagedResponse<IbanChangeResponse>> ListAsync(IbanChangeListFilter filter, CancellationToken cancellationToken)
    {
        LastFilter = filter;
        List<IbanChangeResponse> items =
        [
            .. Changes.Values
                .Where(c => filter.Status is not { } status || (int)c.Status == (int)status)
                .OrderByDescending(c => c.ChangedAt)
                .Select(ToResponse),
        ];
        return Task.FromResult(new PagedResponse<IbanChangeResponse>(items, filter.Page, filter.PageSize, items.Count));
    }

    public Task<IReadOnlyList<IbanChangeResponse>> ForSupplierAsync(SupplierId supplier, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<IbanChangeResponse>>(
            [.. Changes.Values.Where(c => c.SupplierId == supplier).OrderByDescending(c => c.ChangedAt).Select(ToResponse)]);

    private IbanChangeResponse ToResponse(IbanChange c) =>
        new(c.Id.Value, c.SupplierId.Value, parties.Suppliers.GetValueOrDefault(c.SupplierId)?.Name ?? string.Empty, c.PreviousIban, c.NewIban,
            c.CountryChanged, c.ChangedAt, (IbanChangeStatusDto)c.Status, c.DecidedAt, (IbanVerificationMethodDto?)c.Method, c.Note)
        {
            Version = c.Version,
        };
}

/// <summary>Annuaires simulés : enregistrent ce qui leur est demandé.</summary>
internal sealed class FakeDirectories : ICompanyDirectory, IVatNumberChecker
{
    public List<string> Searches { get; } = [];

    public List<VatNumber> Checks { get; } = [];

    public Task<Result<CompanySearchResponse>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        Searches.Add(query);
        return Task.FromResult<Result<CompanySearchResponse>>(new CompanySearchResponse([], 0));
    }

    public Task<Result<VatNumberCheckResponse>> CheckAsync(VatNumber vatNumber, CancellationToken cancellationToken)
    {
        Checks.Add(vatNumber);
        return Task.FromResult<Result<VatNumberCheckResponse>>(new VatNumberCheckResponse(vatNumber.Value, true, null, null, DateTimeOffset.UnixEpoch));
    }
}

internal static class PartiesAndCatalogFakesRegistration
{
    public static IServiceCollection AddPartiesAndCatalogFakes(this IServiceCollection services, InMemoryPartiesAndCatalog store, FakeDirectories directories, InMemoryBilling? billing = null, InMemoryIbanChanges? ibans = null)
    {
        services.AddSingleton<ISupplierRepository>(store);
        services.AddSingleton<ISupplierReadStore>(store);
        services.AddSingleton<ICatalogItemRepository>(store);
        services.AddSingleton<ICatalogItemReadStore>(store);
        InMemoryIbanChanges ibanChanges = ibans ?? new InMemoryIbanChanges(store);
        services.AddSingleton<IIbanChangeRepository>(ibanChanges);
        services.AddSingleton<IIbanChangeReadStore>(ibanChanges);
        services.AddSingleton<ICompanyDirectory>(directories);
        services.AddSingleton<IVatNumberChecker>(directories);
        services.AddBillingFakes(billing ?? new InMemoryBilling());
        return services;
    }
}
