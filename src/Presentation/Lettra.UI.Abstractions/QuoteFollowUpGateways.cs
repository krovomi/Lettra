using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;

namespace Lettra.UI.Abstractions;

/// <summary>Filtre et tri de l'historique des relances de devis.</summary>
public sealed record QuoteFollowUpHistoryQuery(
    Guid? QuoteId = null,
    QuoteFollowUpChannelDto? Channel = null,
    QuoteFollowUpOutcomeDto? Outcome = null,
    DateOnly? From = null,
    DateOnly? To = null,
    string? Search = null,
    QuoteFollowUpSortDto Sort = QuoteFollowUpSortDto.Date,
    bool Descending = true,
    int Page = 1,
    int PageSize = 50);

/// <summary>
/// Devis à relancer et carnet de commandes. Lecture : <c>DossierLire</c> ; relance envoyée ou notée : <c>DevisCreer</c> ;
/// fenêtre des devis à relancer : <c>CatalogueGerer</c>. Les e-mails partent par le port e-mail du serveur (MailPit dans
/// la démo).
/// </summary>
public interface IQuoteFollowUpsGateway
{
    Task<OperationResult<QuoteFollowUpOverviewResponse>> GetOverviewAsync(CancellationToken cancellationToken);

    Task<OperationResult<QuoteFollowUpPreviewResponse>> GetPreviewAsync(Guid quoteId, CancellationToken cancellationToken);

    Task<OperationResult<QuoteFollowUpResponse>> SendAsync(Guid quoteId, SendQuoteFollowUpRequest request, CancellationToken cancellationToken);

    Task<OperationResult<PagedResponse<QuoteFollowUpResponse>>> ListHistoryAsync(QuoteFollowUpHistoryQuery query, CancellationToken cancellationToken);

    Task<OperationResult<QuoteFollowUpSettingsResponse>> UpdateSettingsAsync(UpdateQuoteFollowUpSettingsRequest request, CancellationToken cancellationToken);

    Task<OperationResult<OrderBookResponse>> GetOrderBookAsync(CancellationToken cancellationToken);
}
