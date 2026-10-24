using System.Globalization;
using System.Net.Http.Json;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Directory;
using Lettra.Contracts.Serialization;
using Lettra.Contracts.Suppliers;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpSuppliersGateway(HttpClient http) : ISuppliersGateway
{
    public Task<OperationResult<SupplierResponse>> CreateAsync(CreateSupplierRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync("api/v1/suppliers", request, ContractsJsonContext.Default.CreateSupplierRequest, ct),
            ContractsJsonContext.Default.SupplierResponse,
            cancellationToken);

    public Task<OperationResult<PagedResponse<SupplierSummary>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/suppliers?page={page}&pageSize={pageSize}"), ct),
            ContractsJsonContext.Default.PagedResponseSupplierSummary,
            cancellationToken);

    public Task<OperationResult<PagedResponse<SupplierSummary>>> SearchAsync(PartyListQuery query, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(new Uri("api/v1/suppliers?" + HttpCalls.PartyListParameters(query), UriKind.Relative), ct),
            ContractsJsonContext.Default.PagedResponseSupplierSummary,
            cancellationToken);

    public Task<OperationResult<SupplierResponse>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/suppliers/{id}"), ct),
            ContractsJsonContext.Default.SupplierResponse,
            cancellationToken);

    public Task<OperationResult<SupplierResponse>> UpdateAsync(Guid id, long expectedVersion, CreateSupplierRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PutVersionedAsync(HttpCalls.Relative($"api/v1/suppliers/{id}"), expectedVersion, request, ContractsJsonContext.Default.CreateSupplierRequest, ct),
            ContractsJsonContext.Default.SupplierResponse,
            cancellationToken);

    public Task<OperationResult<bool>> DeleteAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken) =>
        VersionedCalls.DeleteAsync(http, HttpCalls.Relative($"api/v1/suppliers/{id}"), expectedVersion, cancellationToken);
}

internal sealed class HttpCatalogGateway(HttpClient http) : ICatalogGateway
{
    public Task<OperationResult<CatalogReferenceData>> GetReferenceDataAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/catalog/reference-data"), ct),
            ContractsJsonContext.Default.CatalogReferenceData,
            cancellationToken);

    public Task<OperationResult<PagedResponse<CatalogItemSummary>>> ListAsync(CatalogListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        string archived = query.IncludeArchived ? "true" : "false";
        string text = string.Create(CultureInfo.InvariantCulture, $"page={query.Page}&pageSize={query.PageSize}&includeArchived={archived}")
            + HttpCalls.SearchParameter(query.Search);
        if (query.Sort != CatalogSortDto.Reference || query.Direction != SortDirectionDto.Ascending)
        {
            text += string.Create(CultureInfo.InvariantCulture, $"&sort={query.Sort}&direction={query.Direction}");
        }

        if (query.Kind is { } kind)
        {
            text += string.Create(CultureInfo.InvariantCulture, $"&kind={kind}");
        }

        return HttpCalls.SendAsync(
            ct => http.GetAsync(new Uri("api/v1/catalog/items?" + text, UriKind.Relative), ct),
            ContractsJsonContext.Default.PagedResponseCatalogItemSummary,
            cancellationToken);
    }

    public Task<OperationResult<CatalogItemResponse>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/catalog/items/{id}"), ct),
            ContractsJsonContext.Default.CatalogItemResponse,
            cancellationToken);

    public Task<OperationResult<CatalogItemResponse>> CreateAsync(SaveCatalogItemRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync("api/v1/catalog/items", request, ContractsJsonContext.Default.SaveCatalogItemRequest, ct),
            ContractsJsonContext.Default.CatalogItemResponse,
            cancellationToken);

    public Task<OperationResult<CatalogItemResponse>> UpdateAsync(Guid id, long expectedVersion, SaveCatalogItemRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PutVersionedAsync(
                HttpCalls.Relative($"api/v1/catalog/items/{id}"), expectedVersion, request, ContractsJsonContext.Default.SaveCatalogItemRequest, ct),
            ContractsJsonContext.Default.CatalogItemResponse,
            cancellationToken);

    public Task<OperationResult<CatalogItemResponse>> SetArchivedAsync(Guid id, long expectedVersion, bool isArchived, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PutVersionedAsync(
                HttpCalls.Relative($"api/v1/catalog/items/{id}/archived"),
                expectedVersion,
                new SetCatalogItemArchivedRequest(isArchived),
                ContractsJsonContext.Default.SetCatalogItemArchivedRequest,
                ct),
            ContractsJsonContext.Default.CatalogItemResponse,
            cancellationToken);
}

internal sealed class HttpDirectoryGateway(HttpClient http) : IDirectoryGateway
{
    public Task<OperationResult<CompanySearchResponse>> SearchCompaniesAsync(string query, int maxResults, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/directory/companies?q={Uri.EscapeDataString(query ?? string.Empty)}&max={maxResults}"), ct),
            ContractsJsonContext.Default.CompanySearchResponse,
            cancellationToken);

    public Task<OperationResult<VatNumberCheckResponse>> CheckVatNumberAsync(string vatNumber, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/directory/vat-numbers/{Uri.EscapeDataString((vatNumber ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal))}"), ct),
            ContractsJsonContext.Default.VatNumberCheckResponse,
            cancellationToken);
}
