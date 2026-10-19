using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpRecurringInvoicesGateway(HttpClient http) : IRecurringInvoicesGateway
{
    private const string Root = "api/v1/recurring-invoices";

    public Task<OperationResult<PagedResponse<RecurringInvoiceSummary>>> ListAsync(RecurringInvoiceListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"{Root}?{ListQuery(query)}"), ct),
            ContractsJsonContext.Default.PagedResponseRecurringInvoiceSummary,
            cancellationToken);
    }

    public Task<OperationResult<RecurringInvoiceResponse>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.GetAsync(HttpCalls.Relative($"{Root}/{id}"), ct), ContractsJsonContext.Default.RecurringInvoiceResponse, cancellationToken);

    public Task<OperationResult<RecurringInvoiceResponse>> CreateAsync(SaveRecurringInvoiceRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync(Root, request, ContractsJsonContext.Default.SaveRecurringInvoiceRequest, ct),
            ContractsJsonContext.Default.RecurringInvoiceResponse,
            cancellationToken);

    public Task<OperationResult<RecurringInvoiceResponse>> UpdateAsync(Guid id, SaveRecurringInvoiceRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        VersionedCalls.SendAsync(
            http, HttpMethod.Put, HttpCalls.Relative($"{Root}/{id}"), expectedVersion,
            JsonContent.Create(request, ContractsJsonContext.Default.SaveRecurringInvoiceRequest), ContractsJsonContext.Default.RecurringInvoiceResponse, cancellationToken);

    public Task<OperationResult<RecurringInvoiceResponse>> SetActiveAsync(Guid id, bool isActive, long? expectedVersion, CancellationToken cancellationToken) =>
        VersionedCalls.SendAsync(
            http, HttpMethod.Put, HttpCalls.Relative($"{Root}/{id}/active"), expectedVersion,
            JsonContent.Create(new SetRecurringInvoiceActiveRequest { IsActive = isActive }, ContractsJsonContext.Default.SetRecurringInvoiceActiveRequest),
            ContractsJsonContext.Default.RecurringInvoiceResponse, cancellationToken);

    public Task<OperationResult<bool>> DeleteAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken) =>
        VersionedCalls.DeleteAsync(http, HttpCalls.Relative($"{Root}/{id}"), expectedVersion, cancellationToken);

    public Task<OperationResult<RecurringRunResponse>> RunAsync(Guid? id, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsync(id is { } one ? HttpCalls.Relative($"{Root}/{one}/run") : HttpCalls.Relative($"{Root}/run"), null, ct),
            ContractsJsonContext.Default.RecurringRunResponse,
            cancellationToken);

    private static string ListQuery(RecurringInvoiceListQuery query)
    {
        StringBuilder text = new(string.Create(CultureInfo.InvariantCulture, $"page={query.Page}&pageSize={query.PageSize}&sort={query.Sort}"));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            text.Append("&search=").Append(Uri.EscapeDataString(query.Search.Trim()));
        }

        if (query.CustomerId is { } customer)
        {
            text.Append("&customerId=").Append(customer);
        }

        if (query.Active is { } active)
        {
            text.Append("&active=").Append(active ? "true" : "false");
        }

        if (query.DueOnly)
        {
            text.Append("&dueOnly=true");
        }

        if (query.Descending)
        {
            text.Append("&descending=true");
        }

        return text.ToString();
    }
}
