using Lettra.Application.Features.Billing;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Domain.Billing;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Mêmes cas d'usage et mêmes permissions que les routes <c>/quotes/follow-ups</c> et <c>/quotes/order-book</c> de l'API.</summary>
internal sealed class InProcessQuoteFollowUpsGateway(IServiceScopeFactory scopes) : IQuoteFollowUpsGateway
{
    public Task<OperationResult<QuoteFollowUpOverviewResponse>> GetOverviewAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetQuoteFollowUpOverviewQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<QuoteFollowUpPreviewResponse>> GetPreviewAsync(Guid quoteId, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetQuoteFollowUpPreviewQuery(dossier, new QuoteId(quoteId)), ct), cancellationToken);

    public Task<OperationResult<QuoteFollowUpResponse>> SendAsync(Guid quoteId, SendQuoteFollowUpRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new SendQuoteFollowUpCommand(dossier, new QuoteId(quoteId), request), ct), cancellationToken);

    public Task<OperationResult<PagedResponse<QuoteFollowUpResponse>>> ListHistoryAsync(QuoteFollowUpHistoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new ListQuoteFollowUpsQuery(
            dossier, query.QuoteId, null, query.Channel, query.Outcome, query.From, query.To, query.Search, query.Sort, query.Descending, query.Page, query.PageSize), ct), cancellationToken);
    }

    public Task<OperationResult<QuoteFollowUpSettingsResponse>> UpdateSettingsAsync(UpdateQuoteFollowUpSettingsRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new UpdateQuoteFollowUpSettingsCommand(dossier, request), ct), cancellationToken);

    public Task<OperationResult<OrderBookResponse>> GetOrderBookAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetOrderBookQuery(dossier), ct), cancellationToken);
}
