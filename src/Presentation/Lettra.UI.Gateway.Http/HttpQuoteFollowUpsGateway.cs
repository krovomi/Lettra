using System.Net.Http.Json;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpQuoteFollowUpsGateway(HttpClient http) : IQuoteFollowUpsGateway
{
    public Task<OperationResult<QuoteFollowUpOverviewResponse>> GetOverviewAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/quotes/follow-ups"), ct),
            ContractsJsonContext.Default.QuoteFollowUpOverviewResponse,
            cancellationToken);

    public Task<OperationResult<QuoteFollowUpPreviewResponse>> GetPreviewAsync(Guid quoteId, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/quotes/{quoteId}/follow-up/preview"), ct),
            ContractsJsonContext.Default.QuoteFollowUpPreviewResponse,
            cancellationToken);

    public Task<OperationResult<QuoteFollowUpResponse>> SendAsync(Guid quoteId, SendQuoteFollowUpRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync(HttpCalls.Relative($"api/v1/quotes/{quoteId}/follow-up"), request, ContractsJsonContext.Default.SendQuoteFollowUpRequest, ct),
            ContractsJsonContext.Default.QuoteFollowUpResponse,
            cancellationToken);

    public Task<OperationResult<PagedResponse<QuoteFollowUpResponse>>> ListHistoryAsync(QuoteFollowUpHistoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        List<string> parameters =
        [
            $"page={query.Page}",
            $"pageSize={query.PageSize}",
            $"sort={query.Sort}",
            $"descending={(query.Descending ? "true" : "false")}",
        ];
        if (query.QuoteId is Guid quote)
        {
            parameters.Add($"quoteId={quote}");
        }

        if (query.Channel is QuoteFollowUpChannelDto channel)
        {
            parameters.Add($"channel={channel}");
        }

        if (query.Outcome is QuoteFollowUpOutcomeDto outcome)
        {
            parameters.Add($"outcome={outcome}");
        }

        if (query.From is DateOnly from)
        {
            parameters.Add("from=" + QueryText.Day(from));
        }

        if (query.To is DateOnly to)
        {
            parameters.Add("to=" + QueryText.Day(to));
        }

        string url = $"api/v1/quotes/follow-ups/history?{string.Join('&', parameters)}{HttpCalls.SearchParameter(query.Search)}";
        return HttpCalls.SendAsync(ct => http.GetAsync(HttpCalls.Relative($"{url}"), ct), ContractsJsonContext.Default.PagedResponseQuoteFollowUpResponse, cancellationToken);
    }

    public Task<OperationResult<QuoteFollowUpSettingsResponse>> UpdateSettingsAsync(UpdateQuoteFollowUpSettingsRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PutAsJsonAsync(HttpCalls.Relative($"api/v1/quotes/follow-ups/settings"), request, ContractsJsonContext.Default.UpdateQuoteFollowUpSettingsRequest, ct),
            ContractsJsonContext.Default.QuoteFollowUpSettingsResponse,
            cancellationToken);

    public Task<OperationResult<OrderBookResponse>> GetOrderBookAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/quotes/order-book"), ct),
            ContractsJsonContext.Default.OrderBookResponse,
            cancellationToken);
}
