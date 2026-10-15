using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Directory;
using Lettra.Contracts.Suppliers;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests;

internal sealed class FakeSuppliersGateway : ISuppliersGateway
{
    public List<SupplierSummary> Suppliers { get; } = [];

    public IReadOnlyList<ErrorDetail>? NextErrors { get; set; }

    public CreateSupplierRequest? LastRequest { get; private set; }

    public Task<OperationResult<SupplierResponse>> CreateAsync(CreateSupplierRequest request, CancellationToken cancellationToken)
    {
        LastRequest = request with { };
        if (NextErrors is not null)
        {
            return Task.FromResult(OperationResult<SupplierResponse>.Failure(NextErrors));
        }

        SupplierResponse created = new(Guid.CreateVersion7(), request.Name, request.Siren, request.Siret, request.VatNumber, request.Email,
            request.Iban, request.AddressLine1, request.AddressLine2, request.PostalCode, request.City, request.CountryCode, DateTimeOffset.UnixEpoch);
        Suppliers.Add(new SupplierSummary(created.Id, created.Name, created.Siren, created.City));
        return Task.FromResult(OperationResult<SupplierResponse>.Success(created));
    }

    public Task<OperationResult<PagedResponse<SupplierSummary>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<PagedResponse<SupplierSummary>>.Success(new PagedResponse<SupplierSummary>([.. Suppliers], page, pageSize, Suppliers.Count)));
}

internal sealed class FakeCatalogGateway : ICatalogGateway
{
    public static readonly CatalogReferenceData Reference = new(
        ["C62", "HUR", "DAY", "LS"],
        [new VatRateOption("FR-20", 20m), new VatRateOption("FR-10", 10m), new VatRateOption("FR-5.5", 5.5m), new VatRateOption("FR-0", 0m)]);

    public List<CatalogItemResponse> Items { get; } = [];

    public IReadOnlyList<ErrorDetail>? NextErrors { get; set; }

    public List<(Guid Id, SaveCatalogItemRequest Request)> Updates { get; } = [];

    public List<SaveCatalogItemRequest> Creations { get; } = [];

    public List<(Guid Id, bool IsArchived)> ArchiveCalls { get; } = [];

    public List<(string? Search, bool IncludeArchived, int PageSize)> ListCalls { get; } = [];

    public CatalogItemResponse Add(string reference, string designation, decimal price, string vatCode = "FR-20", string unit = "HUR", bool archived = false)
    {
        VatRateOption rate = Reference.VatRates.Single(v => v.Code == vatCode);
        CatalogItemResponse item = new(Guid.CreateVersion7(), reference, designation, "Descriptif " + reference, CatalogItemKindDto.Service, unit, price,
            rate.Code, rate.Percent, archived, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
        {
            Version = 1,
        };
        Items.Add(item);
        return item;
    }

    public Task<OperationResult<CatalogReferenceData>> GetReferenceDataAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<CatalogReferenceData>.Success(Reference));

    public Task<OperationResult<PagedResponse<CatalogItemSummary>>> ListAsync(string? search, bool includeArchived, int page, int pageSize, CancellationToken cancellationToken)
    {
        ListCalls.Add((search, includeArchived, pageSize));
        List<CatalogItemSummary> items = Items
            .Where(i => includeArchived || !i.IsArchived)
            .Where(i => search is null || i.Reference.Contains(search, StringComparison.OrdinalIgnoreCase) || i.Designation.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Select(i => new CatalogItemSummary(i.Id, i.Reference, i.Designation, i.Description, i.Kind, i.UnitCode, i.UnitPriceExclTax, i.VatRateCode, i.VatRatePercent, i.IsArchived)
            {
                Version = i.Version,
            })
            .ToList();
        return Task.FromResult(OperationResult<PagedResponse<CatalogItemSummary>>.Success(new PagedResponse<CatalogItemSummary>(items, page, pageSize, items.Count)));
    }

    public Task<OperationResult<CatalogItemResponse>> CreateAsync(SaveCatalogItemRequest request, CancellationToken cancellationToken)
    {
        Creations.Add(request with { });
        if (Failure() is { } failure)
        {
            return Task.FromResult(failure);
        }

        VatRateOption rate = Reference.VatRates.Single(v => v.Code == request.VatRateCode);
        CatalogItemResponse item = new(Guid.CreateVersion7(), request.Reference, request.Designation, request.Description, request.Kind, request.UnitCode,
            request.UnitPriceExclTax, rate.Code, rate.Percent, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
        {
            Version = 1,
        };
        Items.Add(item);
        return Task.FromResult(OperationResult<CatalogItemResponse>.Success(item));
    }

    public Task<OperationResult<CatalogItemResponse>> UpdateAsync(Guid id, long expectedVersion, SaveCatalogItemRequest request, CancellationToken cancellationToken)
    {
        Updates.Add((id, request with { }));
        if (Failure() is { } failure)
        {
            return Task.FromResult(failure);
        }

        int index = Items.FindIndex(i => i.Id == id);
        if (Items[index].Version != expectedVersion)
        {
            return Task.FromResult(OperationResult<CatalogItemResponse>.Stale(Items[index]));
        }

        VatRateOption rate = Reference.VatRates.Single(v => v.Code == request.VatRateCode);
        CatalogItemResponse item = Items[index] with
        {
            Reference = request.Reference,
            Designation = request.Designation,
            Description = request.Description,
            Kind = request.Kind,
            UnitCode = request.UnitCode,
            UnitPriceExclTax = request.UnitPriceExclTax,
            VatRateCode = rate.Code,
            VatRatePercent = rate.Percent,
            Version = Items[index].Version + 1,
        };
        Items[index] = item;
        return Task.FromResult(OperationResult<CatalogItemResponse>.Success(item));
    }

    public Task<OperationResult<CatalogItemResponse>> SetArchivedAsync(Guid id, long expectedVersion, bool isArchived, CancellationToken cancellationToken)
    {
        ArchiveCalls.Add((id, isArchived));
        if (Failure() is { } failure)
        {
            return Task.FromResult(failure);
        }

        int index = Items.FindIndex(i => i.Id == id);
        if (Items[index].Version != expectedVersion)
        {
            return Task.FromResult(OperationResult<CatalogItemResponse>.Stale(Items[index]));
        }

        Items[index] = Items[index] with { IsArchived = isArchived, Version = Items[index].Version + 1 };
        return Task.FromResult(OperationResult<CatalogItemResponse>.Success(Items[index]));
    }

    /// <summary>Un collègue modifie l'article pendant la saisie (ADR-0013) : la version du serveur avance.</summary>
    public void EditedByColleague(Guid id, Func<CatalogItemResponse, CatalogItemResponse> edit)
    {
        int index = Items.FindIndex(i => i.Id == id);
        Items[index] = edit(Items[index]) with { Version = Items[index].Version + 1 };
    }

    private OperationResult<CatalogItemResponse>? Failure()
    {
        if (NextErrors is null)
        {
            return null;
        }

        OperationResult<CatalogItemResponse> failure = OperationResult<CatalogItemResponse>.Failure(NextErrors);
        NextErrors = null;
        return failure;
    }
}

internal sealed class FakeDirectoryGateway : IDirectoryGateway
{
    public List<CompanySearchResult> Companies { get; } = [];

    public string? SearchError { get; set; }

    public string? VatError { get; set; }

    public VatNumberCheckResponse? VatResponse { get; set; }

    public List<string> Queries { get; } = [];

    public static CompanySearchResult Danone { get; } = new("552032534", "55203253400646", "DANONE", "17 Boulevard Haussmann", null, "75009", "Paris", "FR",
        "FR27552032534", "70.10Z", true);

    public static CompanySearchResult Ancienne { get; } = new("732829320", null, "ANCIENNE SARL", "1 rue Vieille", null, "69001", "Lyon", "FR",
        "FR44732829320", null, false);

    public Task<OperationResult<CompanySearchResponse>> SearchCompaniesAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        Queries.Add(query);
        return Task.FromResult(SearchError is null
            ? OperationResult<CompanySearchResponse>.Success(new CompanySearchResponse([.. Companies.Take(maxResults)], Companies.Count))
            : OperationResult<CompanySearchResponse>.Failure(SearchError));
    }

    public Task<OperationResult<VatNumberCheckResponse>> CheckVatNumberAsync(string vatNumber, CancellationToken cancellationToken) =>
        Task.FromResult(VatError is not null
            ? OperationResult<VatNumberCheckResponse>.Failure(VatError)
            : OperationResult<VatNumberCheckResponse>.Success(VatResponse ?? new VatNumberCheckResponse(vatNumber, true, null, null, DateTimeOffset.UnixEpoch)));
}
