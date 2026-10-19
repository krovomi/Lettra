using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Directory;
using Lettra.Contracts.Serialization;
using Lettra.Contracts.Suppliers;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

/// <summary>Appels HTTP communs : une coupure réseau devient <c>Api.Unreachable</c>, jamais une exception à l'écran.</summary>
internal static class HttpCalls
{
    public static async Task<OperationResult<T>> SendAsync<T>(
        Func<CancellationToken, Task<HttpResponseMessage>> send, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await send(cancellationToken).ConfigureAwait(false);
            return await response.ToOperationResultAsync(typeInfo, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (HttpCalls.IsUnreachable(e, cancellationToken))
        {
            return OperationResult<T>.Failure(ErrorCodes.Api.Unreachable);
        }
    }

    /// <summary>
    /// Serveur injoignable : erreur réseau, ou délai d'attente dépassé (le <see cref="HttpClient"/> annule alors sans
    /// que l'appelant l'ait demandé). Une annulation voulue par l'écran remonte telle quelle.
    /// </summary>
    public static bool IsUnreachable(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    /// <summary>Paramètre <c>&amp;search=</c> échappé, vide sans recherche.</summary>
    public static string SearchParameter(string? search) =>
        ListSearch.Normalize(search) is { } text ? "&search=" + Uri.EscapeDataString(text) : string.Empty;

    /// <summary>Paramètres d'une liste de clients ou de fournisseurs ; le tri n'apparaît que s'il diffère du nom de A à Z.</summary>
    public static string PartyListParameters(PartyListQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        string text = string.Create(CultureInfo.InvariantCulture, $"page={query.Page}&pageSize={query.PageSize}") + SearchParameter(query.Search);
        if (query.Sort != PartySortDto.Name || query.Direction != SortDirectionDto.Ascending)
        {
            text += string.Create(CultureInfo.InvariantCulture, $"&sort={query.Sort}&direction={query.Direction}");
        }

        return query.Kind is { } kind ? string.Create(CultureInfo.InvariantCulture, $"{text}&kind={kind}") : text;
    }

    public static Uri Relative(FormattableString uri) => new(string.Create(CultureInfo.InvariantCulture, $"{uri}"), UriKind.Relative);

    /// <summary>PUT d'une modification avec la version lue en <c>If-Match</c> (ADR-0013).</summary>
    public static Task<HttpResponseMessage> PutVersionedAsync<TBody>(
        this HttpClient http, Uri uri, long expectedVersion, TBody body, JsonTypeInfo<TBody> typeInfo, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new(HttpMethod.Put, uri) { Content = JsonContent.Create(body, typeInfo) };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue(string.Create(CultureInfo.InvariantCulture, $"\"{expectedVersion}\"")));
        return SendAndDisposeAsync(http, request, cancellationToken);
    }

    private static async Task<HttpResponseMessage> SendAndDisposeAsync(HttpClient http, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            return await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}

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
