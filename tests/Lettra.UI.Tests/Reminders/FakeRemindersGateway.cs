using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Reminders;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests.Reminders;

/// <summary>Relances fictives : les tests posent les factures échues et l'historique, et lisent ce que l'écran a demandé.</summary>
internal sealed class FakeRemindersGateway : IRemindersGateway
{
    public List<ReminderInvoiceResponse> Invoices { get; } = [];

    public List<PaymentReminderResponse> History { get; } = [];

    public ReminderSettingsResponse Settings { get; set; } = new(false, [3, 15, 30, 45], 7, true, 0, new ReminderPreventionDto(true, 3, true, 3, 300m));

    public List<UpcomingReminderResponse> Upcoming { get; } = [];

    public List<PaymentPlanResponse> Plans { get; } = [];

    public List<TargetingObjectionResponse> Objections { get; } = [];

    public List<(Guid CustomerId, bool Objects)> ObjectionChanges { get; } = [];

    public List<(Guid PlanId, string Decision, long Version)> Decisions { get; } = [];

    public List<(Guid InvoiceId, ReminderChannelDto Channel)> Sends { get; } = [];

    public List<ReminderHistoryQuery> HistoryQueries { get; } = [];

    public List<UpdateReminderSettingsRequest> SavedSettings { get; } = [];

    public string? FailWith { get; set; }

    public RunRemindersResponse RunResult { get; set; } = new(2, 1, 1, 0);

    public static ReminderInvoiceResponse Invoice(string number, string customer, string? email, int days, ReminderLevelDto? next = ReminderLevelDto.Friendly, bool dueToday = true, ReminderLevelDto? last = null) =>
        new(Guid.CreateVersion7(), number, Guid.CreateVersion7(), customer, email, "fr-FR", new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), 1200m, days,
            last, last is null ? null : new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), next, dueToday, 9.86m, 40m);

    public static UpcomingReminderResponse Coming(string number, string customer, PaymentWeatherDto weather = PaymentWeatherDto.Rainy, bool dueToday = true,
        DayOfWeek? bestDay = null, DateTimeOffset? sentAt = null) =>
        new(Guid.CreateVersion7(), number, Guid.CreateVersion7(), customer, "compta@client.fr", "fr-FR", new DateOnly(2026, 9, 22), new DateOnly(2026, 10, 22), 1500m,
            weather, bestDay, new DateOnly(2026, 10, 19), dueToday, sentAt);

    public static PaymentPlanResponse Plan(string number, string customer, PaymentPlanStatusDto status = PaymentPlanStatusDto.OnTrack, bool acknowledged = false) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), number, Guid.CreateVersion7(), customer, status, 1200m, status == PaymentPlanStatusDto.Completed ? 0m : 800m,
            [
                new(1, new DateOnly(2026, 10, 15), 400m, true, false, 0m),
                new(2, new DateOnly(2026, 11, 15), 400m, false, status is PaymentPlanStatusDto.Late or PaymentPlanStatusDto.Lapsed, 400m),
                new(3, new DateOnly(2026, 12, 15), 400m, false, false, 400m),
            ],
            "Jean Durand", new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero), acknowledged,
            status == PaymentPlanStatusDto.Cancelled ? new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero) : null, 1);

    public static PaymentReminderResponse Sent(string number, string customer, ReminderLevelDto level = ReminderLevelDto.Friendly, ReminderOutcomeDto outcome = ReminderOutcomeDto.Sent,
        ReminderTriggerDto trigger = ReminderTriggerDto.User) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), number, Guid.CreateVersion7(), customer, level, ReminderChannelDto.Email, outcome,
            outcome == ReminderOutcomeDto.Failed ? "Email.SendFailed" : null, "compta@client.fr", "fr-FR", $"Rappel : facture {number}", "Bonjour,\nCorps", 1200m, 20, trigger,
            new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero));

    public Task<OperationResult<ReminderOverviewResponse>> GetOverviewAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<ReminderOverviewResponse>.Success(new([.. Invoices], Settings, Invoices.Count(i => i.DueToday), [.. Upcoming], [.. Plans], [.. Objections])));

    public Task<OperationResult<ReminderPreviewResponse>> GetPreviewAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        if (FailWith is not null)
        {
            return Task.FromResult(OperationResult<ReminderPreviewResponse>.Failure(FailWith));
        }

        if (Upcoming.FirstOrDefault(u => u.InvoiceId == invoiceId) is { } coming)
        {
            return Task.FromResult(OperationResult<ReminderPreviewResponse>.Success(new(invoiceId, ReminderLevelDto.BeforeDue, coming.Language, coming.CustomerEmail,
                $"Petit rappel : facture {coming.Number} à régler bientôt", "Bonjour,\n\nLa facture arrive à échéance…")));
        }

        ReminderInvoiceResponse? invoice = Invoices.FirstOrDefault(i => i.InvoiceId == invoiceId);
        return Task.FromResult(invoice is null
            ? OperationResult<ReminderPreviewResponse>.Failure(ErrorCodes.Reminders.InvoiceNotOverdue)
            : OperationResult<ReminderPreviewResponse>.Success(new(invoiceId, invoice.NextLevel!.Value, invoice.Language, invoice.CustomerEmail,
                $"Rappel : facture {invoice.Number} arrivée à échéance", "Bonjour,\n\nSauf erreur de notre part…")));
    }

    public Task<OperationResult<PaymentReminderResponse>> SendAsync(Guid invoiceId, ReminderChannelDto channel, CancellationToken cancellationToken)
    {
        Sends.Add((invoiceId, channel));
        if (FailWith is not null)
        {
            return Task.FromResult(OperationResult<PaymentReminderResponse>.Failure(FailWith));
        }

        if (Upcoming.FirstOrDefault(u => u.InvoiceId == invoiceId) is { } coming)
        {
            PaymentReminderResponse early = Sent(coming.Number, coming.CustomerName, ReminderLevelDto.BeforeDue) with { Channel = channel };
            History.Insert(0, early);
            Upcoming[Upcoming.IndexOf(coming)] = coming with { DueToday = false, SentAt = early.OccurredAt };
            return Task.FromResult(OperationResult<PaymentReminderResponse>.Success(early));
        }

        ReminderInvoiceResponse invoice = Invoices.Single(i => i.InvoiceId == invoiceId);
        PaymentReminderResponse sent = Sent(invoice.Number, invoice.CustomerName, invoice.NextLevel!.Value) with
        {
            Channel = channel,
            Recipient = channel == ReminderChannelDto.Email ? invoice.CustomerEmail : null,
        };
        History.Insert(0, sent);
        Invoices[Invoices.IndexOf(invoice)] = invoice with { LastLevel = invoice.NextLevel, NextLevel = invoice.NextLevel + 1, DueToday = false };
        return Task.FromResult(OperationResult<PaymentReminderResponse>.Success(sent));
    }

    public Task<OperationResult<RunRemindersResponse>> RunDueAsync(CancellationToken cancellationToken) =>
        Task.FromResult(FailWith is null ? OperationResult<RunRemindersResponse>.Success(RunResult) : OperationResult<RunRemindersResponse>.Failure(FailWith));

    public Task<OperationResult<PagedResponse<PaymentReminderResponse>>> ListHistoryAsync(ReminderHistoryQuery query, CancellationToken cancellationToken)
    {
        HistoryQueries.Add(query);
        List<PaymentReminderResponse> matching = [.. History.Where(r => query.Level is null || r.Level == query.Level)];
        return Task.FromResult(OperationResult<PagedResponse<PaymentReminderResponse>>.Success(
            new([.. matching.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)], query.Page, query.PageSize, matching.Count)));
    }

    public Task<OperationResult<ReminderSettingsResponse>> UpdateSettingsAsync(UpdateReminderSettingsRequest request, CancellationToken cancellationToken)
    {
        SavedSettings.Add(request);
        if (FailWith is not null)
        {
            return Task.FromResult(OperationResult<ReminderSettingsResponse>.Failure(FailWith));
        }

        Settings = new ReminderSettingsResponse(request.AutoSend, [.. request.Delays], request.MinimumGapDays, Settings.EmailConfigured, Settings.Version + 1,
            request.Prevention ?? Settings.Prevention);
        return Task.FromResult(OperationResult<ReminderSettingsResponse>.Success(Settings));
    }

    public Task<OperationResult<InvoicePaymentPlanResponse>> GetInvoicePlanAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<InvoicePaymentPlanResponse>.Success(new(Plans.FirstOrDefault(p => p.InvoiceId == invoiceId))));

    public Task<OperationResult<PaymentPlanResponse>> AcknowledgePlanAsync(Guid planId, long version, CancellationToken cancellationToken) =>
        DecideAsync(planId, "acknowledge", version, p => p with { Acknowledged = true });

    public Task<OperationResult<PaymentPlanResponse>> CancelPlanAsync(Guid planId, long version, CancellationToken cancellationToken) =>
        DecideAsync(planId, "cancel", version, p => p with
        {
            Status = PaymentPlanStatusDto.Cancelled,
            Acknowledged = true,
            CancelledAt = new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero),
        });

    public Task<OperationResult<bool>> SetObjectionAsync(Guid customerId, bool objects, CancellationToken cancellationToken)
    {
        ObjectionChanges.Add((customerId, objects));
        if (FailWith is not null)
        {
            return Task.FromResult(OperationResult<bool>.Failure(FailWith));
        }

        Objections.RemoveAll(o => o.CustomerId == customerId);
        if (objects)
        {
            string name = Upcoming.FirstOrDefault(u => u.CustomerId == customerId)?.CustomerName ?? "Client";
            Objections.Add(new(customerId, name, new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero)));
            Upcoming.RemoveAll(u => u.CustomerId == customerId);
        }

        return Task.FromResult(OperationResult<bool>.Success(true));
    }

    private Task<OperationResult<PaymentPlanResponse>> DecideAsync(Guid planId, string decision, long version, Func<PaymentPlanResponse, PaymentPlanResponse> change)
    {
        Decisions.Add((planId, decision, version));
        if (FailWith is not null)
        {
            return Task.FromResult(OperationResult<PaymentPlanResponse>.Failure(FailWith));
        }

        int index = Plans.FindIndex(p => p.Id == planId);
        Plans[index] = change(Plans[index]) with { Version = Plans[index].Version + 1 };
        return Task.FromResult(OperationResult<PaymentPlanResponse>.Success(Plans[index]));
    }
}
