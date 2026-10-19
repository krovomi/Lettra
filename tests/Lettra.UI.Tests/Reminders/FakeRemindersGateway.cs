using Lettra.Contracts.Common;
using Lettra.Contracts.Reminders;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests.Reminders;

/// <summary>Relances fictives : les tests posent les factures échues et l'historique, et lisent ce que l'écran a demandé.</summary>
internal sealed class FakeRemindersGateway : IRemindersGateway
{
    public List<ReminderInvoiceResponse> Invoices { get; } = [];

    public List<PaymentReminderResponse> History { get; } = [];

    public ReminderSettingsResponse Settings { get; set; } = new(false, [3, 15, 30, 45], 7, true, 0);

    public List<(Guid InvoiceId, ReminderChannelDto Channel)> Sends { get; } = [];

    public List<ReminderHistoryQuery> HistoryQueries { get; } = [];

    public List<UpdateReminderSettingsRequest> SavedSettings { get; } = [];

    public string? FailWith { get; set; }

    public RunRemindersResponse RunResult { get; set; } = new(2, 1, 1, 0);

    public static ReminderInvoiceResponse Invoice(string number, string customer, string? email, int days, ReminderLevelDto? next = ReminderLevelDto.Friendly, bool dueToday = true, ReminderLevelDto? last = null) =>
        new(Guid.CreateVersion7(), number, Guid.CreateVersion7(), customer, email, "fr-FR", new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), 1200m, days,
            last, last is null ? null : new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), next, dueToday, 9.86m, 40m);

    public static PaymentReminderResponse Sent(string number, string customer, ReminderLevelDto level = ReminderLevelDto.Friendly, ReminderOutcomeDto outcome = ReminderOutcomeDto.Sent,
        ReminderTriggerDto trigger = ReminderTriggerDto.User) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), number, Guid.CreateVersion7(), customer, level, ReminderChannelDto.Email, outcome,
            outcome == ReminderOutcomeDto.Failed ? "Email.SendFailed" : null, "compta@client.fr", "fr-FR", $"Rappel : facture {number}", "Bonjour,\nCorps", 1200m, 20, trigger,
            new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero));

    public Task<OperationResult<ReminderOverviewResponse>> GetOverviewAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<ReminderOverviewResponse>.Success(new([.. Invoices], Settings, Invoices.Count(i => i.DueToday))));

    public Task<OperationResult<ReminderPreviewResponse>> GetPreviewAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        ReminderInvoiceResponse? invoice = Invoices.FirstOrDefault(i => i.InvoiceId == invoiceId);
        return Task.FromResult(invoice is null || FailWith is not null
            ? OperationResult<ReminderPreviewResponse>.Failure(FailWith ?? ErrorCodes.Reminders.InvoiceNotOverdue)
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

        Settings = new ReminderSettingsResponse(request.AutoSend, [.. request.Delays], request.MinimumGapDays, Settings.EmailConfigured, Settings.Version + 1);
        return Task.FromResult(OperationResult<ReminderSettingsResponse>.Success(Settings));
    }
}
