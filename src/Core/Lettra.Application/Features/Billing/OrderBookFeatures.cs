using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Billing;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Billing;

/// <summary>
/// Le carnet de commandes pondéré : le chiffre d'affaires à venir des devis en attente, pondéré par les taux
/// d'acceptation passés, avec ces taux pour l'expliquer. Même droit que la liste des devis. C'est la requête que la
/// prévision de trésorerie reprendra (montants TTC et fins de validité).
/// </summary>
public sealed record GetOrderBookQuery(DossierId Dossier) : IQuery<Result<OrderBookResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

internal sealed class GetOrderBookHandler(IQuotePipelineReader pipeline, TimeProvider clock) : IQueryHandler<GetOrderBookQuery, Result<OrderBookResponse>>
{
    public async Task<Result<OrderBookResponse>> HandleAsync(GetOrderBookQuery query, CancellationToken cancellationToken)
    {
        DateOnly today = LegalCalendar.Today(clock);
        IReadOnlyList<QuoteFact> facts = await pipeline.ListAsync(OrderBookCalculator.HistoryFrom(today), cancellationToken).ConfigureAwait(false);
        return OrderBookCalculator.Estimate(facts, today).ToResponse(today);
    }
}

internal static class OrderBookMapping
{
    public static OrderBookResponse ToResponse(this OrderBookEstimate estimate, DateOnly today) => new(
        today,
        estimate.HistoryFrom,
        OrderBookCalculator.HistoryMonths,
        OrderBookCalculator.MinimumSample,
        estimate.Overall.Accepted,
        estimate.Overall.Closed,
        estimate.GlobalRate,
        estimate.HasEnoughHistory,
        estimate.OpenExclTax,
        estimate.WeightedExclTax,
        estimate.WeightedInclTax,
        [.. estimate.ByAmount.Select(b => new AmountBandRateDto((QuoteAmountBandDto)b.Band, b.Count.Accepted, b.Count.Closed, b.Count.Rate))],
        [.. estimate.ByAge.Select(b => new AgeBandRateDto((QuoteAgeBandDto)b.Band, b.Count.Accepted, b.Count.Closed, b.Count.Rate))],
        [
            .. estimate.Quotes.Select(w => new OrderBookQuoteResponse(
                w.Quote.Id.Value,
                w.Quote.Number,
                w.Quote.CustomerId.Value,
                w.Quote.CustomerName,
                w.Quote.IssueDate,
                w.Quote.ValidUntil!.Value,
                w.AgeDays,
                w.Quote.AmountExclTax,
                w.Quote.AmountInclTax,
                (QuoteAmountBandDto)w.AmountBand,
                (QuoteAgeBandDto)w.AgeBand,
                w.Customer.ToDto(),
                w.Amount.ToDto(),
                w.Age.ToDto(),
                w.Probability,
                w.WeightedExclTax,
                w.WeightedInclTax)),
        ]);

    private static CriterionRateDto ToDto(this CriterionRate rate) => new(rate.Observed.Accepted, rate.Observed.Closed, rate.Rate, rate.UsesGlobalRate);
}
