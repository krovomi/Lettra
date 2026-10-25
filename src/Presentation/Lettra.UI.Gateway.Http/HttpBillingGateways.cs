using System.Net.Http.Json;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Demo;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Pricing;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpQuotesGateway(HttpClient http) : IQuotesGateway
{
    public Task<OperationResult<PagedResponse<QuoteSummary>>> ListAsync(BillingListQuery query, QuoteStatusDto? status, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(new Uri("api/v1/quotes?" + VersionedCalls.ListQuery(query, ("status", status?.ToString())), UriKind.Relative), ct),
            ContractsJsonContext.Default.PagedResponseQuoteSummary,
            cancellationToken);

    public Task<OperationResult<QuoteResponse>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.GetAsync(HttpCalls.Relative($"api/v1/quotes/{id}"), ct), ContractsJsonContext.Default.QuoteResponse, cancellationToken);

    public Task<OperationResult<QuoteResponse>> CreateAsync(SaveQuoteRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync("api/v1/quotes", request, ContractsJsonContext.Default.SaveQuoteRequest, ct),
            ContractsJsonContext.Default.QuoteResponse,
            cancellationToken);

    public Task<OperationResult<QuoteResponse>> UpdateAsync(Guid id, SaveQuoteRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        VersionedCalls.SendAsync(
            http, HttpMethod.Put, HttpCalls.Relative($"api/v1/quotes/{id}"), expectedVersion,
            JsonContent.Create(request, ContractsJsonContext.Default.SaveQuoteRequest), ContractsJsonContext.Default.QuoteResponse, cancellationToken);

    public Task<OperationResult<bool>> DeleteAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken) =>
        VersionedCalls.DeleteAsync(http, HttpCalls.Relative($"api/v1/quotes/{id}"), expectedVersion, cancellationToken);

    public Task<OperationResult<ComplianceReportResponse>> CheckAsync(Guid id, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/quotes/{id}/compliance"), ct),
            ContractsJsonContext.Default.ComplianceReportResponse,
            cancellationToken);

    public Task<OperationResult<QuoteResponse>> ValidateAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken) =>
        VersionedCalls.SendAsync(
            http, HttpMethod.Post, HttpCalls.Relative($"api/v1/quotes/{id}/validate"), expectedVersion, null, ContractsJsonContext.Default.QuoteResponse, cancellationToken);

    public Task<OperationResult<QuoteResponse>> SetOutcomeAsync(Guid id, bool accepted, long? expectedVersion, CancellationToken cancellationToken) =>
        VersionedCalls.SendAsync(
            http, HttpMethod.Put, HttpCalls.Relative($"api/v1/quotes/{id}/outcome"), expectedVersion,
            JsonContent.Create(new SetQuoteOutcomeRequest(accepted), ContractsJsonContext.Default.SetQuoteOutcomeRequest),
            ContractsJsonContext.Default.QuoteResponse,
            cancellationToken);

    public Task<OperationResult<InvoiceResponse>> CreateDownPaymentAsync(Guid id, CreateDownPaymentRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync(HttpCalls.Relative($"api/v1/quotes/{id}/down-payments"), request, ContractsJsonContext.Default.CreateDownPaymentRequest, ct),
            ContractsJsonContext.Default.InvoiceResponse,
            cancellationToken);

    public Task<OperationResult<InvoiceResponse>> CreateInvoiceAsync(Guid id, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsync(HttpCalls.Relative($"api/v1/quotes/{id}/invoice"), null, ct),
            ContractsJsonContext.Default.InvoiceResponse,
            cancellationToken);
}

internal sealed class HttpInvoicesGateway(HttpClient http) : IInvoicesGateway
{
    public Task<OperationResult<PagedResponse<InvoiceSummary>>> ListAsync(BillingListQuery query, InvoiceKindDto? kind, InvoiceStatusDto? status, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(new Uri("api/v1/invoices?" + VersionedCalls.ListQuery(query, ("kind", kind?.ToString()), ("status", status?.ToString())), UriKind.Relative), ct),
            ContractsJsonContext.Default.PagedResponseInvoiceSummary,
            cancellationToken);

    public Task<OperationResult<InvoiceResponse>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.GetAsync(HttpCalls.Relative($"api/v1/invoices/{id}"), ct), ContractsJsonContext.Default.InvoiceResponse, cancellationToken);

    public Task<OperationResult<InvoiceResponse>> CreateAsync(SaveInvoiceRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync("api/v1/invoices", request, ContractsJsonContext.Default.SaveInvoiceRequest, ct),
            ContractsJsonContext.Default.InvoiceResponse,
            cancellationToken);

    public Task<OperationResult<InvoiceResponse>> UpdateAsync(Guid id, SaveInvoiceRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        VersionedCalls.SendAsync(
            http, HttpMethod.Put, HttpCalls.Relative($"api/v1/invoices/{id}"), expectedVersion,
            JsonContent.Create(request, ContractsJsonContext.Default.SaveInvoiceRequest), ContractsJsonContext.Default.InvoiceResponse, cancellationToken);

    public Task<OperationResult<bool>> DeleteAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken) =>
        VersionedCalls.DeleteAsync(http, HttpCalls.Relative($"api/v1/invoices/{id}"), expectedVersion, cancellationToken);

    public Task<OperationResult<ComplianceReportResponse>> CheckAsync(Guid id, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/invoices/{id}/compliance"), ct),
            ContractsJsonContext.Default.ComplianceReportResponse,
            cancellationToken);

    public Task<OperationResult<InvoiceResponse>> ValidateAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken) =>
        VersionedCalls.SendAsync(
            http, HttpMethod.Post, HttpCalls.Relative($"api/v1/invoices/{id}/validate"), expectedVersion, null, ContractsJsonContext.Default.InvoiceResponse, cancellationToken);

    public Task<OperationResult<InvoiceResponse>> IssueAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken) =>
        VersionedCalls.SendAsync(
            http, HttpMethod.Post, HttpCalls.Relative($"api/v1/invoices/{id}/issue"), expectedVersion, null, ContractsJsonContext.Default.InvoiceResponse, cancellationToken);

    public Task<OperationResult<InvoiceResponse>> CreateCreditNoteAsync(Guid id, CreateCreditNoteRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync(HttpCalls.Relative($"api/v1/invoices/{id}/credit-notes"), request, ContractsJsonContext.Default.CreateCreditNoteRequest, ct),
            ContractsJsonContext.Default.InvoiceResponse,
            cancellationToken);

    public Task<OperationResult<InvoiceResponse>> ApproveCreditAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken) =>
        VersionedCalls.SendAsync(
            http, HttpMethod.Post, HttpCalls.Relative($"api/v1/invoices/{id}/approve-credit"), expectedVersion, null, ContractsJsonContext.Default.InvoiceResponse, cancellationToken);

    public Task<OperationResult<byte[]>> GetEInvoiceAsync(Guid id, EInvoiceFormatDto format, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/invoices/{id}/e-invoice?format={format}"), ct),
            async (response, ct) => response.IsSuccessStatusCode
                ? OperationResult<byte[]>.Success(await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false))
                : await response.FailureAsync<byte[]>(ct).ConfigureAwait(false),
            cancellationToken);
}

internal sealed class HttpPlatformTransmissionGateway(HttpClient http) : IPlatformTransmissionGateway
{
    public Task<OperationResult<PlatformTransmissionResponse>> GetAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/invoices/{invoiceId}/platform-transmission"), ct),
            ContractsJsonContext.Default.PlatformTransmissionResponse,
            cancellationToken);

    public Task<OperationResult<PlatformTransmissionResponse>> TransmitAsync(Guid invoiceId, TransmitInvoiceRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync(HttpCalls.Relative($"api/v1/invoices/{invoiceId}/platform-transmission"), request, ContractsJsonContext.Default.TransmitInvoiceRequest, ct),
            ContractsJsonContext.Default.PlatformTransmissionResponse,
            cancellationToken);

    public Task<OperationResult<PlatformTransmissionResponse>> RefreshAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsync(HttpCalls.Relative($"api/v1/invoices/{invoiceId}/platform-transmission/refresh"), null, ct),
            ContractsJsonContext.Default.PlatformTransmissionResponse,
            cancellationToken);

    public Task<OperationResult<PlatformTransmissionResponse>> ReportPaymentAsync(Guid invoiceId, ReportPaymentRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync(HttpCalls.Relative($"api/v1/invoices/{invoiceId}/platform-transmission/payments"), request, ContractsJsonContext.Default.ReportPaymentRequest, ct),
            ContractsJsonContext.Default.PlatformTransmissionResponse,
            cancellationToken);
}

internal sealed class HttpBillingDashboardGateway(HttpClient http) : IBillingDashboardGateway
{
    public Task<OperationResult<BillingDashboardResponse>> GetAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/billing/dashboard"), ct),
            ContractsJsonContext.Default.BillingDashboardResponse,
            cancellationToken);
}

internal sealed class HttpBillingSettingsGateway(HttpClient http) : IBillingSettingsGateway
{
    public Task<OperationResult<BillingReferenceData>> GetReferenceDataAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/billing/reference-data"), ct),
            ContractsJsonContext.Default.BillingReferenceData,
            cancellationToken);

    public Task<OperationResult<CompanyProfileResponse>> GetCompanyProfileAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/company-profile"), ct),
            ContractsJsonContext.Default.CompanyProfileResponse,
            cancellationToken);

    public Task<OperationResult<CompanyProfileResponse>> SaveCompanyProfileAsync(SaveCompanyProfileRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        VersionedCalls.SendAsync(
            http, HttpMethod.Put, HttpCalls.Relative($"api/v1/company-profile"), expectedVersion,
            JsonContent.Create(request, ContractsJsonContext.Default.SaveCompanyProfileRequest), ContractsJsonContext.Default.CompanyProfileResponse, cancellationToken);
}

internal sealed class HttpPriceListsGateway(HttpClient http) : IPriceListsGateway
{
    public Task<OperationResult<PagedResponse<PriceListSummary>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/price-lists?page={page}&pageSize={pageSize}"), ct),
            ContractsJsonContext.Default.PagedResponsePriceListSummary,
            cancellationToken);

    public Task<OperationResult<PriceListResponse>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.GetAsync(HttpCalls.Relative($"api/v1/price-lists/{id}"), ct), ContractsJsonContext.Default.PriceListResponse, cancellationToken);

    public Task<OperationResult<PriceListResponse>> CreateAsync(SavePriceListRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync("api/v1/price-lists", request, ContractsJsonContext.Default.SavePriceListRequest, ct),
            ContractsJsonContext.Default.PriceListResponse,
            cancellationToken);

    public Task<OperationResult<PriceListResponse>> UpdateAsync(Guid id, SavePriceListRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        VersionedCalls.SendAsync(
            http, HttpMethod.Put, HttpCalls.Relative($"api/v1/price-lists/{id}"), expectedVersion,
            JsonContent.Create(request, ContractsJsonContext.Default.SavePriceListRequest), ContractsJsonContext.Default.PriceListResponse, cancellationToken);

    public Task<OperationResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        VersionedCalls.DeleteAsync(http, HttpCalls.Relative($"api/v1/price-lists/{id}"), null, cancellationToken);

    public Task<OperationResult<IReadOnlyList<ResolvedPrice>>> ResolveAsync(Guid customerId, IReadOnlyCollection<Guid> catalogItemIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalogItemIds);
        string items = string.Concat(catalogItemIds.Select(id => "&itemId=" + id.ToString()));
        return HttpCalls.SendAsync(
            ct => http.GetAsync(new Uri($"api/v1/price-lists/resolve?customerId={customerId}{items}", UriKind.Relative), ct),
            ContractsJsonContext.Default.IReadOnlyListResolvedPrice,
            cancellationToken);
    }
}

internal sealed class HttpReceivablesGateway(HttpClient http) : IReceivablesGateway
{
    public Task<OperationResult<ReceivablesSummaryResponse>> GetSummaryAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/receivables/summary"), ct),
            ContractsJsonContext.Default.ReceivablesSummaryResponse,
            cancellationToken);
}

internal sealed class HttpDemoDataGateway(HttpClient http) : IDemoDataGateway
{
    public Task<OperationResult<DemoDataStatusResponse>> GetStatusAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/demo"), ct),
            ContractsJsonContext.Default.DemoDataStatusResponse,
            cancellationToken);

    public Task<OperationResult<DemoSeedResponse>> SeedAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsync(HttpCalls.Relative($"api/v1/demo/seed"), null, ct),
            ContractsJsonContext.Default.DemoSeedResponse,
            cancellationToken);
}
