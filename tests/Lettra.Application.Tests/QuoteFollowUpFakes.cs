using Lettra.Application.Abstractions.Mail;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Common;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Tests;

/// <summary>
/// Faux dépôts des devis à relancer : les devis numérotés lus dans le faux de facturation, les coordonnées des clients
/// posées par le test, l'historique et le réglage gardés en mémoire.
/// </summary>
internal sealed class InMemoryQuoteFollowUps(InMemoryBilling billing) : IQuotePipelineReader, IQuoteFollowUpRepository, IQuoteFollowUpSettingsRepository
{
    public Dictionary<CustomerId, QuoteCustomerContact> Contacts { get; } = [];

    public List<QuoteFollowUp> FollowUps { get; } = [];

    public QuoteFollowUpSettings? Settings { get; private set; }

    /// <summary>Les clients opposés au profilage, posés par le test.</summary>
    public HashSet<CustomerId> Objecting { get; } = [];

    public Task<IReadOnlyList<QuoteFact>> ListAsync(DateOnly from, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<QuoteFact>>(
        [
            .. billing.Quotes.Values
                .Where(q => q.Number is not null && q.IssueDate is { } issued && (issued >= from || q.Status == QuoteStatus.Validated))
                .Select(q => new QuoteFact(
                    q.Id, q.Number!, q.CustomerId, q.Customer.Name, q.Status, q.IssueDate!.Value, q.ValidUntil, q.RespondedOn, q.TotalExclTax, q.TotalInclTax)),
        ]);

    public Task<IReadOnlyDictionary<CustomerId, QuoteCustomerContact>> ContactsAsync(IReadOnlyCollection<CustomerId> customers, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<CustomerId, QuoteCustomerContact>>(
            customers.Where(Contacts.ContainsKey).ToDictionary(c => c, c => Contacts[c]));

    public Task<IReadOnlySet<CustomerId>> ObjectingCustomersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<CustomerId>>(Objecting);

    public Task AddAsync(QuoteFollowUp followUp, CancellationToken cancellationToken)
    {
        FollowUps.Add(followUp);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<QuoteId, QuoteFollowUpStats>> StatsAsync(IReadOnlyCollection<QuoteId> quotes, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<QuoteId, QuoteFollowUpStats>>(
            FollowUps.Where(f => quotes.Contains(f.QuoteId) && f.Outcome == QuoteFollowUpOutcome.Sent)
                .GroupBy(f => f.QuoteId)
                .ToDictionary(g => g.Key, g => new QuoteFollowUpStats(g.Count(), g.MaxBy(f => f.OccurredAt)!)));

    public Task<(IReadOnlyList<QuoteFollowUpHistoryItem> Items, int Total)> ListAsync(QuoteFollowUpHistoryFilter filter, CancellationToken cancellationToken)
    {
        QuoteFollowUpHistoryItem[] items =
        [
            .. FollowUps
                .Where(f => filter.Quote is not { } q || f.QuoteId == q)
                .Where(f => filter.Channel is not { } c || f.Channel == c)
                .Where(f => filter.Search is not { } s || f.QuoteNumber.Contains(s, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.OccurredAt)
                .Select(f => new QuoteFollowUpHistoryItem(f, billing.Quotes[f.QuoteId].Customer.Name)),
        ];
        return Task.FromResult<(IReadOnlyList<QuoteFollowUpHistoryItem>, int)>(
            ([.. items.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)], items.Length));
    }

    public Task<QuoteFollowUpSettings?> FindAsync(CancellationToken cancellationToken) => Task.FromResult(Settings);

    public Task AddAsync(QuoteFollowUpSettings settings, CancellationToken cancellationToken)
    {
        Settings = settings;
        return Task.CompletedTask;
    }
}

/// <summary>Un serveur d'envoi qui garde les messages, configuré ou non, et qui peut refuser l'envoi.</summary>
internal sealed class CapturingEmailSender : IEmailSender
{
    public bool IsConfigured { get; set; } = true;

    public bool Refuse { get; set; }

    public List<EmailMessage> Sent { get; } = [];

    public Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (Refuse)
        {
            return Task.FromResult(Result.Failure(new Error(ErrorCodes.Email.SendFailed, ErrorKind.Unexpected)));
        }

        Sent.Add(message);
        return Task.FromResult(Result.Success());
    }
}

/// <summary>Une horloge que le test avance.</summary>
internal sealed class SettableClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
