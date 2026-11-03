using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests.Billing;

/// <summary>Devis à relancer fictifs : les tests posent les devis, le carnet et l'historique, et lisent ce que l'écran a demandé.</summary>
internal sealed class FakeQuoteFollowUpsGateway : IQuoteFollowUpsGateway
{
    public List<QuoteToFollowUpResponse> Quotes { get; } = [];

    public List<QuoteFollowUpResponse> History { get; } = [];

    public QuoteFollowUpSettingsResponse Settings { get; set; } = new(5, true, 0);

    public OrderBookResponse? Book { get; set; }

    public List<(Guid QuoteId, SendQuoteFollowUpRequest Request)> Sends { get; } = [];

    public List<QuoteFollowUpHistoryQuery> HistoryQueries { get; } = [];

    public List<UpdateQuoteFollowUpSettingsRequest> SavedSettings { get; } = [];

    public string? FailWith { get; set; }

    public static QuoteToFollowUpResponse Quote(string number, string customer, string? email, int daysLeft, decimal amount = 1_200m, bool due = true, decimal probability = 0.5m) =>
        new(Guid.CreateVersion7(), number, Guid.CreateVersion7(), customer, email, "fr-FR", new DateOnly(2026, 9, 20), new DateOnly(2026, 10, 16).AddDays(daysLeft),
            daysLeft, 26, amount, amount * 1.2m, probability, amount * probability, due, due ? 0 : 1, due ? null : new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero),
            due ? null : QuoteFollowUpChannelDto.Email);

    public static QuoteFollowUpResponse Sent(string number, string customer, QuoteFollowUpChannelDto channel = QuoteFollowUpChannelDto.Email,
        QuoteFollowUpOutcomeDto outcome = QuoteFollowUpOutcomeDto.Sent, decimal amount = 1_200m) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), number, Guid.CreateVersion7(), customer, channel, outcome,
            outcome == QuoteFollowUpOutcomeDto.Failed ? "Email.SendFailed" : null, channel == QuoteFollowUpChannelDto.Email ? "contact@client.fr" : null, "fr-FR",
            $"Votre devis {number}", "Bonjour,\nCorps", amount, new DateOnly(2026, 10, 19), new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero));

    public static OrderBookQuoteResponse BookQuote(string number, string customer, decimal amount, decimal probability, int age = 10, bool customerGlobal = false) =>
        new(Guid.CreateVersion7(), number, Guid.CreateVersion7(), customer, new DateOnly(2026, 10, 16).AddDays(-age), new DateOnly(2026, 11, 15), age, amount, amount * 1.2m,
            amount < 1_000m ? QuoteAmountBandDto.Small : QuoteAmountBandDto.Medium, age <= 7 ? QuoteAgeBandDto.FirstWeek : QuoteAgeBandDto.SecondWeek,
            new CriterionRateDto(customerGlobal ? 1 : 3, customerGlobal ? 2 : 4, customerGlobal ? 0.5m : 0.75m, customerGlobal),
            new CriterionRateDto(2, 4, 0.5m, false), new CriterionRateDto(1, 4, 0.25m, false), probability, amount * probability, amount * 1.2m * probability);

    public Task<OperationResult<QuoteFollowUpOverviewResponse>> GetOverviewAsync(CancellationToken cancellationToken)
    {
        QuoteToFollowUpResponse[] due = [.. Quotes.Where(q => q.Due)];
        return Task.FromResult(OperationResult<QuoteFollowUpOverviewResponse>.Success(new([.. Quotes], Settings, due.Length, due.Sum(q => q.TotalExclTax))));
    }

    public Task<OperationResult<QuoteFollowUpPreviewResponse>> GetPreviewAsync(Guid quoteId, CancellationToken cancellationToken)
    {
        QuoteToFollowUpResponse? quote = Quotes.FirstOrDefault(q => q.QuoteId == quoteId);
        return Task.FromResult(quote is null || FailWith is not null
            ? OperationResult<QuoteFollowUpPreviewResponse>.Failure(FailWith ?? ErrorCodes.QuoteFollowUps.QuoteNotPending)
            : OperationResult<QuoteFollowUpPreviewResponse>.Success(new(quoteId, quote.Number, quote.CustomerName, quote.CustomerEmail, quote.Language,
                $"Votre devis {quote.Number} : valable jusqu'au {quote.ValidUntil:d}", "Bonjour,\n\nNous revenons vers vous…", quote.DaysLeft, Settings.EmailConfigured)));
    }

    public Task<OperationResult<QuoteFollowUpResponse>> SendAsync(Guid quoteId, SendQuoteFollowUpRequest request, CancellationToken cancellationToken)
    {
        Sends.Add((quoteId, request));
        if (FailWith is not null)
        {
            return Task.FromResult(OperationResult<QuoteFollowUpResponse>.Failure(FailWith));
        }

        QuoteToFollowUpResponse quote = Quotes.Single(q => q.QuoteId == quoteId);
        QuoteFollowUpResponse sent = Sent(quote.Number, quote.CustomerName, request.Channel) with
        {
            QuoteId = quoteId,
            Recipient = request.Channel == QuoteFollowUpChannelDto.Email ? quote.CustomerEmail : null,
            Subject = request.Subject ?? "",
        };
        History.Insert(0, sent);
        Quotes[Quotes.IndexOf(quote)] = quote with { Due = false, FollowUpCount = quote.FollowUpCount + 1, LastFollowUpAt = sent.OccurredAt, LastChannel = request.Channel };
        return Task.FromResult(OperationResult<QuoteFollowUpResponse>.Success(sent));
    }

    public Task<OperationResult<PagedResponse<QuoteFollowUpResponse>>> ListHistoryAsync(QuoteFollowUpHistoryQuery query, CancellationToken cancellationToken)
    {
        HistoryQueries.Add(query);
        List<QuoteFollowUpResponse> matching = [.. History.Where(f => (query.QuoteId is null || f.QuoteId == query.QuoteId) && (query.Channel is null || f.Channel == query.Channel))];
        return Task.FromResult(OperationResult<PagedResponse<QuoteFollowUpResponse>>.Success(
            new([.. matching.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)], query.Page, query.PageSize, matching.Count)));
    }

    public Task<OperationResult<QuoteFollowUpSettingsResponse>> UpdateSettingsAsync(UpdateQuoteFollowUpSettingsRequest request, CancellationToken cancellationToken)
    {
        SavedSettings.Add(request);
        if (FailWith is not null)
        {
            return Task.FromResult(OperationResult<QuoteFollowUpSettingsResponse>.Failure(FailWith));
        }

        Settings = Settings with { WindowDays = request.WindowDays, Version = Settings.Version + 1 };
        return Task.FromResult(OperationResult<QuoteFollowUpSettingsResponse>.Success(Settings));
    }

    public Task<OperationResult<OrderBookResponse>> GetOrderBookAsync(CancellationToken cancellationToken) =>
        Task.FromResult(FailWith is not null || Book is null
            ? OperationResult<OrderBookResponse>.Failure(FailWith ?? ErrorCodes.Api.Unexpected)
            : OperationResult<OrderBookResponse>.Success(Book));
}
