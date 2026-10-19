using System.Globalization;
using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Reminders;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpRemindersGateway(HttpClient http) : IRemindersGateway
{
    public Task<OperationResult<ReminderOverviewResponse>> GetOverviewAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.GetAsync(HttpCalls.Relative($"api/v1/reminders"), ct), ContractsJsonContext.Default.ReminderOverviewResponse, cancellationToken);

    public Task<OperationResult<ReminderPreviewResponse>> GetPreviewAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/reminders/invoices/{invoiceId}/preview"), ct),
            ContractsJsonContext.Default.ReminderPreviewResponse,
            cancellationToken);

    public Task<OperationResult<PaymentReminderResponse>> SendAsync(Guid invoiceId, ReminderChannelDto channel, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync(HttpCalls.Relative($"api/v1/reminders/invoices/{invoiceId}"), new SendReminderRequest(channel), ContractsJsonContext.Default.SendReminderRequest, ct),
            ContractsJsonContext.Default.PaymentReminderResponse,
            cancellationToken);

    public Task<OperationResult<RunRemindersResponse>> RunDueAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.PostAsync(HttpCalls.Relative($"api/v1/reminders/run"), null, ct), ContractsJsonContext.Default.RunRemindersResponse, cancellationToken);

    public Task<OperationResult<PagedResponse<PaymentReminderResponse>>> ListHistoryAsync(ReminderHistoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        List<string> parameters =
        [
            $"page={query.Page}",
            $"pageSize={query.PageSize}",
            $"sort={query.Sort}",
            $"descending={(query.Descending ? "true" : "false")}",
        ];
        if (query.InvoiceId is Guid invoice)
        {
            parameters.Add($"invoiceId={invoice}");
        }

        if (query.Level is ReminderLevelDto level)
        {
            parameters.Add($"level={level}");
        }

        if (query.Outcome is ReminderOutcomeDto outcome)
        {
            parameters.Add($"outcome={outcome}");
        }

        if (query.Channel is ReminderChannelDto channel)
        {
            parameters.Add($"channel={channel}");
        }

        if (query.From is DateOnly from)
        {
            parameters.Add("from=" + from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (query.To is DateOnly to)
        {
            parameters.Add("to=" + to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        string url = $"api/v1/reminders/history?{string.Join('&', parameters)}{HttpCalls.SearchParameter(query.Search)}";
        return HttpCalls.SendAsync(ct => http.GetAsync(HttpCalls.Relative($"{url}"), ct), ContractsJsonContext.Default.PagedResponsePaymentReminderResponse, cancellationToken);
    }

    public Task<OperationResult<ReminderSettingsResponse>> UpdateSettingsAsync(UpdateReminderSettingsRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PutAsJsonAsync("api/v1/reminders/settings", request, ContractsJsonContext.Default.UpdateReminderSettingsRequest, ct),
            ContractsJsonContext.Default.ReminderSettingsResponse,
            cancellationToken);
}
