using System.Globalization;
using System.Net.Http.Json;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpBankingGateway(HttpClient http) : IBankingGateway
{
    public Task<OperationResult<PagedResponse<PaymentSummary>>> ListPaymentsAsync(PaymentListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        string from = query.From is DateOnly f ? "&from=" + Day(f) : string.Empty;
        string to = query.To is DateOnly t ? "&to=" + Day(t) : string.Empty;
        string customer = query.CustomerId is Guid id ? $"&customerId={id}" : string.Empty;
        string amounts = (query.MinAmount is decimal min ? "&minAmount=" + min.ToString(CultureInfo.InvariantCulture) : string.Empty)
            + (query.MaxAmount is decimal max ? "&maxAmount=" + max.ToString(CultureInfo.InvariantCulture) : string.Empty);
        string sort = query.Sort != PaymentSortDto.Date || query.Direction != SortDirectionDto.Descending
            ? $"&sort={query.Sort}&direction={query.Direction}"
            : string.Empty;
        return HttpCalls.SendAsync(
            ct => http.GetAsync(
                HttpCalls.Relative($"api/v1/payments?page={query.Page}&pageSize={query.PageSize}{from}{to}{customer}{HttpCalls.SearchParameter(query.Search)}{amounts}{sort}"),
                ct),
            ContractsJsonContext.Default.PagedResponsePaymentSummary,
            cancellationToken);
    }

    public Task<OperationResult<PaymentResponse>> GetPaymentAsync(Guid id, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.GetAsync(HttpCalls.Relative($"api/v1/payments/{id}"), ct), ContractsJsonContext.Default.PaymentResponse, cancellationToken);

    public Task<OperationResult<IReadOnlyList<OpenInvoiceResponse>>> GetOpenInvoicesAsync(Guid? customerId, CancellationToken cancellationToken)
    {
        string customer = customerId is Guid id ? $"?customerId={id}" : string.Empty;
        return HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/payments/open-invoices{customer}"), ct),
            ContractsJsonContext.Default.IReadOnlyListOpenInvoiceResponse,
            cancellationToken);
    }

    public Task<OperationResult<PaymentResponse>> RecordPaymentAsync(RecordPaymentRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync("api/v1/payments", request, ContractsJsonContext.Default.RecordPaymentRequest, ct),
            ContractsJsonContext.Default.PaymentResponse,
            cancellationToken);

    public Task<OperationResult<PaymentResponse>> ReversePaymentAsync(Guid id, ReversePaymentRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync(HttpCalls.Relative($"api/v1/payments/{id}/reversal"), request, ContractsJsonContext.Default.ReversePaymentRequest, ct),
            ContractsJsonContext.Default.PaymentResponse,
            cancellationToken);

    public Task<OperationResult<BankImportResponse>> ImportStatementAsync(ImportBankStatementRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync("api/v1/bank/statements", request, ContractsJsonContext.Default.ImportBankStatementRequest, ct),
            ContractsJsonContext.Default.BankImportResponse,
            cancellationToken);

    public Task<OperationResult<BankImportResponse>> ImportDemoStatementAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.PostAsync(HttpCalls.Relative($"api/v1/bank/demo-statement"), null, ct), ContractsJsonContext.Default.BankImportResponse, cancellationToken);

    public Task<OperationResult<ReconciliationResponse>> GetReconciliationAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.GetAsync(HttpCalls.Relative($"api/v1/bank/reconciliation"), ct), ContractsJsonContext.Default.ReconciliationResponse, cancellationToken);

    public Task<OperationResult<ConfirmMatchesResponse>> ConfirmMatchesAsync(ConfirmMatchesRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync("api/v1/bank/reconciliation/confirm", request, ContractsJsonContext.Default.ConfirmMatchesRequest, ct),
            ContractsJsonContext.Default.ConfirmMatchesResponse,
            cancellationToken);

    public Task<OperationResult<BankLineResponse>> IgnoreLineAsync(Guid lineId, bool ignored, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync(
                HttpCalls.Relative($"api/v1/bank/lines/{lineId}/ignore"), new IgnoreBankLineRequest { Ignored = ignored }, ContractsJsonContext.Default.IgnoreBankLineRequest, ct),
            ContractsJsonContext.Default.BankLineResponse,
            cancellationToken);

    public Task<OperationResult<BankingSettingsResponse>> GetSettingsAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.GetAsync(HttpCalls.Relative($"api/v1/bank/settings"), ct), ContractsJsonContext.Default.BankingSettingsResponse, cancellationToken);

    public Task<OperationResult<BankingSettingsResponse>> UpdateSettingsAsync(UpdateBankingSettingsRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PutAsJsonAsync("api/v1/bank/settings", request, ContractsJsonContext.Default.UpdateBankingSettingsRequest, ct),
            ContractsJsonContext.Default.BankingSettingsResponse,
            cancellationToken);

    public Task<OperationResult<BankReconciliationStatusResponse>> GetStatusAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.GetAsync(HttpCalls.Relative($"api/v1/bank/status"), ct), ContractsJsonContext.Default.BankReconciliationStatusResponse, cancellationToken);

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
