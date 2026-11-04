using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using static Lettra.Api.Infrastructure.HttpResults;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Devis à relancer et carnet de commandes : les devis sans réponse dont la fin de validité approche, la relance proposée
/// puis envoyée par e-mail ou notée, l'historique filtré et trié, le réglage de la fenêtre, et le carnet de commandes
/// pondéré par les taux d'acceptation passés. Lecture : <c>DossierLire</c> ; relance : <c>DevisCreer</c> ; réglage :
/// <c>CatalogueGerer</c>.
/// </summary>
internal static class QuoteFollowUpEndpoints
{
    public static RouteGroupBuilder MapQuoteFollowUps(this RouteGroupBuilder quotes)
    {
        quotes.MapGet("/follow-ups", (ITenantContext tenant, IQueryDispatcher queries, CancellationToken ct) =>
                QueryAsync(queries, new GetQuoteFollowUpOverviewQuery(tenant.Current.DossierId), ct))
            .RequirePermission(Permission.DossierLire)
            .WithName("GetQuoteFollowUpOverview")
            .Produces<QuoteFollowUpOverviewResponse>();

        quotes.MapGet("/follow-ups/history", ListHistoryAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("ListQuoteFollowUps")
            .Produces<PagedResponse<QuoteFollowUpResponse>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        quotes.MapPut("/follow-ups/settings", (UpdateQuoteFollowUpSettingsRequest request, ITenantContext tenant, ICommandDispatcher commands, CancellationToken ct) =>
                SendAsync(commands, new UpdateQuoteFollowUpSettingsCommand(tenant.Current.DossierId, request), ct))
            .RequirePermission(Permission.CatalogueGerer)
            .WithName("UpdateQuoteFollowUpSettings")
            .Produces<QuoteFollowUpSettingsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        quotes.MapGet("/{id:guid}/follow-up/preview", (Guid id, ITenantContext tenant, IQueryDispatcher queries, CancellationToken ct) =>
                QueryAsync(queries, new GetQuoteFollowUpPreviewQuery(tenant.Current.DossierId, new QuoteId(id)), ct))
            .RequirePermission(Permission.DossierLire)
            .WithName("GetQuoteFollowUpPreview")
            .Produces<QuoteFollowUpPreviewResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        quotes.MapPost("/{id:guid}/follow-up", (Guid id, SendQuoteFollowUpRequest request, ITenantContext tenant, ICommandDispatcher commands, CancellationToken ct) =>
                SendAsync(commands, new SendQuoteFollowUpCommand(tenant.Current.DossierId, new QuoteId(id), request), ct))
            .RequirePermission(Permission.DevisCreer)
            .WithName("SendQuoteFollowUp")
            .Produces<QuoteFollowUpResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        quotes.MapGet("/order-book", (ITenantContext tenant, IQueryDispatcher queries, CancellationToken ct) =>
                QueryAsync(queries, new GetOrderBookQuery(tenant.Current.DossierId), ct))
            .RequirePermission(Permission.DossierLire)
            .WithName("GetOrderBook")
            .Produces<OrderBookResponse>();

        return quotes;
    }

    private static Task<IResult> ListHistoryAsync(
        ITenantContext tenant,
        IQueryDispatcher queries,
        CancellationToken cancellationToken,
        Guid? quoteId = null,
        Guid? customerId = null,
        QuoteFollowUpChannelDto? channel = null,
        QuoteFollowUpOutcomeDto? outcome = null,
        DateOnly? from = null,
        DateOnly? to = null,
        string? search = null,
        QuoteFollowUpSortDto sort = QuoteFollowUpSortDto.Date,
        bool descending = true,
        int page = 1,
        int pageSize = 50) =>
        QueryAsync(queries, new ListQuoteFollowUpsQuery(
            tenant.Current.DossierId, quoteId, customerId, channel, outcome, from, to, search, sort, descending, page, pageSize), cancellationToken);

    private static async Task<IResult> QueryAsync<T>(IQueryDispatcher queries, IQuery<Result<T>> query, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(query, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> SendAsync<T>(ICommandDispatcher commands, ICommand<Result<T>> command, CancellationToken cancellationToken) =>
        Ok(await commands.SendAsync(command, cancellationToken).ConfigureAwait(false));
}
