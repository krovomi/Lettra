using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Mail;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Features;
using Lettra.Calculations;
using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Reminders;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Reminders;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Banking;

namespace Lettra.Exports.Application.Reminders;

/// <summary>Une relance écrite, avec son client, son signataire et ses pièces jointes (le QR code de virement).</summary>
public sealed record PreparedReminder(ReminderDraft Draft, ReminderCustomer Customer, ReminderSender Sender, IReadOnlyList<EmailAttachment>? Attachments = null);

/// <summary>Une facture encore due vue par les relances : ce qu'elle a reçu, son taux de pénalités, son échéancier en cours.</summary>
internal sealed record OpenInvoice(ReceivableDocument Document, ReminderCustomer Customer, ReminderTrack Track, decimal? PenaltyRate, PaymentPlan? Plan);

/// <summary>
/// Une facture échue vue par les relances. Sous un échéancier en cours, elle n'est échue que si une échéance est
/// manquée (<paramref name="MissedInstallment"/>) : <paramref name="DueDate"/> est la date de cette échéance et
/// <paramref name="Amount"/> ce qui manque ; une seule relance par échéance manquée, sans escalade. Un échéancier non
/// tenu (dernière échéance passée) rend la facture à l'escalade, comptée depuis sa dernière échéance ; un échéancier
/// arrêté la rend à l'escalade comptée depuis son échéance d'origine.
/// </summary>
internal sealed record OverdueInvoice(OpenInvoice Open, DateOnly DueDate, int DaysOverdue, decimal Amount, int? MissedInstallment, PaymentPlanProgress? Progress)
{
    public ReceivableDocument Document => Open.Document;

    public ReminderCustomer Customer => Open.Customer;

    public ReminderState Escalation => Open.Track.Escalation(DaysOverdue);

    public ReminderLevel? NextLevel => MissedInstallment is int number
        ? Open.Track.InstallmentReminded(number) ? null : ReminderLevel.MissedInstallment
        : ReminderPlanner.Next(Escalation.LastLevel);

    public bool DueToday(ReminderSettings settings, DateOnly today) => MissedInstallment is not null
        ? NextLevel is not null && DaysOverdue >= settings.FriendlyDays
        : ReminderPlanner.Due(Escalation, settings, today) is not null;

    public LatePenalty Penalty => LatePenaltyCalculator.Compute(Amount, Open.PenaltyRate, DaysOverdue, Customer.IsProfessional);
}

/// <summary>Un rappel avant échéance possible : la facture, le jour prévu, la météo du client.</summary>
internal sealed record UpcomingInvoice(OpenInvoice Open, DateOnly DueDate, DateOnly PlannedOn, CustomerPaymentWeatherResponse Weather)
{
    public bool Sent => Open.Track.BeforeDue is not null;

    public bool DueToday(DateOnly today) => !Sent && today >= PlannedOn && today < DueDate;
}

/// <summary>Les relances d'un dossier à une date : les factures échues et les rappels avant échéance.</summary>
internal sealed record ReminderBook(
    ReminderSettings Settings,
    IReadOnlyList<OverdueInvoice> Overdue,
    IReadOnlyList<UpcomingInvoice> Upcoming,
    IReadOnlyList<OpenInvoice> Open,
    IReadOnlyDictionary<InvoiceId, ReceivableDocument> Documents)
{
    public int DueToday(DateOnly today) =>
        Overdue.Count(o => o.DueToday(Settings, today)) + Upcoming.Count(u => u.DueToday(today));
}

/// <summary>
/// Les relances d'un dossier : quelles factures sont échues (le reste dû tient compte des encaissements et des avoirs),
/// quel niveau leur envoyer, le texte dans la langue du client, l'envoi par le port e-mail et la trace de chaque envoi.
/// Avant le retard : le rappel avant échéance aux clients ciblés et une relance par échéance d'échéancier manquée, avec
/// le lien de l'espace client et le QR code de virement. Partagé par les cas d'usage de l'écran et par la passe
/// quotidienne du Worker.
/// </summary>
public sealed class PaymentReminderService(
    IReceivableDocumentReader receivables,
    IReminderContextReader context,
    IPaymentReminderRepository reminders,
    IReminderSettingsRepository settingsStore,
    IPaymentPlanRepository plans,
    IPaymentTargetingObjectionRepository objections,
    ReminderTargeting targeting,
    ReminderPaymentAids aids,
    IEmailSender email,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    /// <summary>Les rappels avant échéance montrés : ceux prévus dans les deux semaines, et ceux déjà partis.</summary>
    public const int UpcomingWindowDays = 14;

    /// <summary>Les échéanciers montrés : tous ceux en cours, et ceux demandés dans les trois derniers mois.</summary>
    public const int PlanHistoryDays = 90;

    public bool EmailConfigured => email.IsConfigured;

    public DateOnly Today => LegalCalendar.Today(clock);

    public async Task<ReminderSettings> SettingsAsync(CancellationToken cancellationToken) =>
        await settingsStore.FindAsync(cancellationToken).ConfigureAwait(false) ?? ReminderSettings.Default();

    public ReminderSettingsResponse Describe(ReminderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new(settings.AutoSend, [settings.FriendlyDays, settings.ReminderDays, settings.FinalDays, settings.FormalNoticeDays],
            settings.MinimumGapDays, email.IsConfigured, settings.Version, settings.Prevention.ToDto());
    }

    public async Task<ReminderOverviewResponse> OverviewAsync(CancellationToken cancellationToken)
    {
        DateOnly today = Today;
        ReminderBook book = await BookAsync(today, cancellationToken).ConfigureAwait(false);
        List<ReminderInvoiceResponse> rows = [.. book.Overdue.Select(o => Describe(o, book.Settings, today))];
        List<UpcomingReminderResponse> upcoming =
        [
            .. book.Upcoming
                .Where(u => u.Sent || u.PlannedOn.DayNumber <= today.DayNumber + UpcomingWindowDays)
                .Select(u => Describe(u, today)),
        ];
        IReadOnlyList<PaymentPlan> listed = await plans.ListAsync(clock.GetUtcNow().AddDays(-PlanHistoryDays), cancellationToken).ConfigureAwait(false);
        List<PaymentPlanResponse> planRows =
        [
            .. listed.OrderByDescending(p => p.RequestedAt)
                .Select(p => PaymentPlanViews.Describe(p, book.Documents.GetValueOrDefault(p.InvoiceId), today)),
        ];
        IReadOnlyList<PaymentTargetingObjection> objected = await objections.ListAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<CustomerId, ReminderCustomer> names = objected.Count == 0
            ? new Dictionary<CustomerId, ReminderCustomer>()
            : await context.CustomersAsync([.. objected.Select(o => o.CustomerId)], cancellationToken).ConfigureAwait(false);
        List<TargetingObjectionResponse> objectionRows =
        [
            .. objected.Select(o => new TargetingObjectionResponse(o.CustomerId.Value, names.GetValueOrDefault(o.CustomerId)?.Name ?? "", o.RecordedAt))
                .OrderBy(o => o.CustomerName, StringComparer.CurrentCultureIgnoreCase),
        ];
        return new ReminderOverviewResponse(rows, Describe(book.Settings), book.DueToday(today), upcoming, planRows, objectionRows);
    }

    /// <summary>
    /// Les signaux « Avant le retard » du jour, pour « Décisions à prendre » : les rappels avant échéance à envoyer, les
    /// échéanciers que l'entreprise n'a pas encore vus et les échéances manquées à relancer. Lecture seule.
    /// </summary>
    public async Task<IReadOnlyList<PaymentPreventionSignal>> SignalsAsync(CancellationToken cancellationToken)
    {
        DateOnly today = Today;
        ReminderBook book = await BookAsync(today, cancellationToken).ConfigureAwait(false);
        List<PaymentPreventionSignal> signals =
        [
            .. book.Upcoming.Where(u => u.DueToday(today)).Select(u => PaymentPreventionSignals.BeforeDue(u.Open.Document, u.Open.Customer.Name, u.DueDate)),
            .. book.Overdue.Where(o => o.MissedInstallment is not null && o.DueToday(book.Settings, today))
                .Select(o => PaymentPreventionSignals.MissedInstallment(o.Document, o.Customer.Name, o.MissedInstallment!.Value, o.Amount, o.DueDate)),
        ];
        IReadOnlyList<PaymentPlan> listed = await plans.ListAsync(clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        signals.AddRange(listed.Where(p => p.Status == PaymentPlanStatus.Active && !p.IsAcknowledged)
            .Select(p => PaymentPreventionSignals.PlanToReview(p, book.Documents.GetValueOrDefault(p.InvoiceId)?.CustomerName ?? "")));
        return signals;
    }

    /// <summary>La prochaine relance d'une facture (échue, ou à rappeler avant l'échéance), écrite dans la langue du client.</summary>
    public async Task<Result<PreparedReminder>> DraftAsync(InvoiceId invoice, CancellationToken cancellationToken) =>
        await PrepareAsync(invoice, null, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Envoie (ou note) la prochaine relance d'une facture et la garde dans l'historique. Un e-mail refusé est gardé
    /// comme échec et répond <c>Email.SendFailed</c> ; un client sans adresse, ou une installation sans envoi d'e-mails,
    /// ne laisse aucune trace : rien n'a été tenté.
    /// </summary>
    public async Task<Result<PaymentReminder>> SendAsync(InvoiceId invoice, ReminderChannel channel, ReminderTrigger trigger, Guid userId, CancellationToken cancellationToken)
    {
        if (channel == ReminderChannel.Email && !email.IsConfigured)
        {
            return EmailErrors.NotConfigured;
        }

        Result<PreparedReminder> drafted = await PrepareAsync(invoice, channel == ReminderChannel.Email ? userId : null, cancellationToken).ConfigureAwait(false);
        return drafted.IsFailure
            ? Result<PaymentReminder>.Failure(drafted.Errors)
            : await DeliverAsync(drafted.Value, channel, trigger, userId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// La passe des relances dues aujourd'hui : envoi par e-mail aux clients qui ont une adresse, escalade, échéances
    /// manquées et (s'il est activé) rappels avant échéance.
    /// </summary>
    public async Task<Result<RunRemindersResponse>> RunDueAsync(ReminderTrigger trigger, Guid userId, CancellationToken cancellationToken)
    {
        if (!email.IsConfigured)
        {
            return EmailErrors.NotConfigured;
        }

        ReminderSender? sender = await context.SenderAsync(cancellationToken).ConfigureAwait(false);
        if (sender is null)
        {
            return ReminderErrors.CompanyProfileMissing;
        }

        DateOnly today = Today;
        ReminderBook book = await BookAsync(today, cancellationToken).ConfigureAwait(false);
        List<(OpenInvoice Open, Func<Task<PreparedReminder>> Prepare)> due =
        [
            .. book.Overdue.Where(o => o.DueToday(book.Settings, today))
                .Select(o => (o.Open, (Func<Task<PreparedReminder>>)(() => WriteAsync(o, o.NextLevel!.Value, sender, userId, cancellationToken)))),
            .. book.Upcoming.Where(u => u.DueToday(today))
                .Select(u => (u.Open, (Func<Task<PreparedReminder>>)(() => WriteAsync(u, sender, book.Settings, userId, cancellationToken)))),
        ];
        int sent = 0, failed = 0, withoutEmail = 0;
        foreach ((OpenInvoice open, Func<Task<PreparedReminder>> prepare) in due)
        {
            if (open.Customer.Email is null)
            {
                withoutEmail++;
                continue;
            }

            Result<PaymentReminder> delivered = await DeliverAsync(await prepare().ConfigureAwait(false), ReminderChannel.Email, trigger, userId, cancellationToken).ConfigureAwait(false);
            if (delivered.IsSuccess)
            {
                sent++;
            }
            else
            {
                failed++;
            }
        }

        return new RunRemindersResponse(due.Count, sent, failed, withoutEmail);
    }

    /// <summary>Les relances du dossier à une date : factures échues, rappels avant échéance, avec les échéanciers en cours.</summary>
    internal async Task<ReminderBook> BookAsync(DateOnly today, CancellationToken cancellationToken)
    {
        ReminderSettings settings = await SettingsAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<InvoiceId, ReceivableDocument> all = (await receivables.ListAsync(null, cancellationToken).ConfigureAwait(false))
            .Where(d => d.Kind != InvoiceKind.CreditNote)
            .ToDictionary(d => d.InvoiceId);
        List<ReceivableDocument> documents = [.. all.Values.Where(d => d.Outstanding > 0m && d.DueDate is not null)];
        if (documents.Count == 0)
        {
            return new ReminderBook(settings, [], [], [], all);
        }

        InvoiceId[] ids = [.. documents.Select(d => d.InvoiceId)];
        IReadOnlyDictionary<InvoiceId, PaymentPlan> active = await plans.ActiveAsync(ids, cancellationToken).ConfigureAwait(false);
        bool anyOverdue = documents.Any(d => d.DueDate < today || active.ContainsKey(d.InvoiceId));
        bool anyUpcoming = settings.BeforeDueEnabled && documents.Any(d => d.DueDate >= today && !active.ContainsKey(d.InvoiceId));
        if (!anyOverdue && !anyUpcoming)
        {
            return new ReminderBook(settings, [], [], [], all);
        }

        IReadOnlyDictionary<InvoiceId, ReminderTrack> tracks = await reminders.TracksAsync(ids, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<InvoiceId, decimal> rates = await context.PenaltyRatesAsync(ids, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<CustomerId, ReminderCustomer> customers = await context
            .CustomersAsync([.. documents.Select(d => d.CustomerId).Distinct()], cancellationToken).ConfigureAwait(false);
        List<OpenInvoice> open =
        [
            .. documents.Select(d => new OpenInvoice(
                d,
                customers.GetValueOrDefault(d.CustomerId) ?? new ReminderCustomer(d.CustomerId, d.CustomerName, null, null, IsProfessional: true),
                tracks.GetValueOrDefault(d.InvoiceId) ?? ReminderTrack.Empty,
                rates.TryGetValue(d.InvoiceId, out decimal rate) ? rate : null,
                active.GetValueOrDefault(d.InvoiceId))),
        ];
        List<OverdueInvoice> overdue =
        [
            .. open.Select(o => Overdue(o, today)).OfType<OverdueInvoice>()
                .OrderByDescending(o => o.DaysOverdue)
                .ThenBy(o => o.Document.Number, StringComparer.Ordinal),
        ];
        TargetingSnapshot targets = anyUpcoming ? await targeting.ReadAsync(today, cancellationToken).ConfigureAwait(false) : TargetingSnapshot.Empty;
        List<UpcomingInvoice> upcoming =
        [
            .. open.Where(o => o.Plan is null && o.Document.DueDate >= today && settings.BeforeDueEnabled)
                .Select(o => Upcoming(o, settings, targets))
                .OfType<UpcomingInvoice>()
                .OrderBy(u => u.PlannedOn)
                .ThenBy(u => u.Open.Document.Number, StringComparer.Ordinal),
        ];
        return new ReminderBook(settings, overdue, upcoming, open, all);
    }

    private static OverdueInvoice? Overdue(OpenInvoice open, DateOnly today)
    {
        DateOnly due = open.Document.DueDate!.Value;
        if (open.Plan is not { } plan)
        {
            return due < today ? new OverdueInvoice(open, due, today.DayNumber - due.DayNumber, open.Document.Outstanding, null, null) : null;
        }

        PaymentPlanProgress progress = plan.Progress(open.Document.Outstanding, today);
        if (progress.Lapsed)
        {
            return new OverdueInvoice(open, plan.FinalDueDate, today.DayNumber - plan.FinalDueDate.DayNumber, open.Document.Outstanding, null, progress);
        }

        return progress.LastMissed is { } missed
            ? new OverdueInvoice(open, missed.Installment.DueDate, today.DayNumber - missed.Installment.DueDate.DayNumber, progress.AmountLate, missed.Installment.Number, progress)
            : null;
    }

    private static UpcomingInvoice? Upcoming(OpenInvoice open, ReminderSettings settings, TargetingSnapshot targets)
    {
        if (open.Track.BeforeDue is null && !targets.Targets(open.Customer.Id))
        {
            return null;
        }

        CustomerPaymentWeatherResponse? weather = targets.WeatherOf(open.Customer.Id);
        DateOnly due = open.Document.DueDate!.Value;
        DateOnly? planned = ReminderPlanner.BeforeDueOn(open.Document.IssueDate, due, settings.BeforeDueDays, weather?.BestSendDay);
        if (planned is null && open.Track.BeforeDue is null)
        {
            return null;
        }

        CustomerPaymentWeatherResponse shown = weather ?? new CustomerPaymentWeatherResponse(
            open.Customer.Id.Value, open.Customer.Name, PaymentWeatherDto.Unknown, 0, 0, 0, 0, 0, null, 0, 0m, []);
        return new UpcomingInvoice(open, due, planned ?? open.Track.BeforeDue!.OccurredOn, shown);
    }

    private async Task<Result<PreparedReminder>> PrepareAsync(InvoiceId invoice, Guid? sendAs, CancellationToken cancellationToken)
    {
        DateOnly today = Today;
        ReminderBook book = await BookAsync(today, cancellationToken).ConfigureAwait(false);
        OverdueInvoice? overdue = book.Overdue.FirstOrDefault(o => o.Document.InvoiceId == invoice);
        UpcomingInvoice? upcoming = book.Upcoming.FirstOrDefault(u => u.Open.Document.InvoiceId == invoice && !u.Sent && today < u.DueDate);
        if (overdue is null && upcoming is null)
        {
            return ReminderErrors.InvoiceNotOverdue;
        }

        if (overdue is not null && overdue.NextLevel is null)
        {
            return ReminderErrors.NoLevelLeft;
        }

        ReminderSender? sender = await context.SenderAsync(cancellationToken).ConfigureAwait(false);
        if (sender is null)
        {
            return ReminderErrors.CompanyProfileMissing;
        }

        return overdue is not null
            ? await WriteAsync(overdue, overdue.NextLevel!.Value, sender, sendAs, cancellationToken).ConfigureAwait(false)
            : await WriteAsync(upcoming!, sender, book.Settings, sendAs, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<PaymentReminder>> DeliverAsync(
        PreparedReminder prepared, ReminderChannel channel, ReminderTrigger trigger, Guid userId, CancellationToken cancellationToken)
    {
        (ReminderDraft draft, ReminderCustomer customer, ReminderSender sender, IReadOnlyList<EmailAttachment>? attachments) = prepared;
        string? recipient = null;
        string? errorCode = null;
        if (channel == ReminderChannel.Email)
        {
            if (customer.Email is null)
            {
                return ReminderErrors.CustomerEmailMissing;
            }

            if (!email.IsConfigured)
            {
                return EmailErrors.NotConfigured;
            }

            recipient = customer.Email;
            Result<Email> to = Email.Create(customer.Email);
            Result result = to.IsFailure
                ? Result.Failure(to.Errors)
                : await email.SendAsync(new EmailMessage(to.Value, draft.Subject, draft.Body, SenderName: sender.Name, Attachments: attachments), cancellationToken).ConfigureAwait(false);
            errorCode = result.IsFailure ? result.Errors[0].Code : null;
        }

        PaymentReminder reminder = PaymentReminder.Record(draft, channel, recipient, errorCode, trigger, userId, clock.GetUtcNow(), Today);
        await reminders.AddAsync(reminder, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return errorCode is null ? reminder : EmailErrors.SendFailed;
    }

    /// <summary>Une relance de l'escalade, ou d'échéance manquée (avec le lien de l'espace client et le QR code).</summary>
    private async Task<PreparedReminder> WriteAsync(
        OverdueInvoice invoice, ReminderLevel level, ReminderSender sender, Guid? sendAs, CancellationToken cancellationToken)
    {
        string language = ReminderTemplates.LanguageOf(invoice.Customer.Language);
        ReceivableDocument d = invoice.Document;
        bool help = level == ReminderLevel.MissedInstallment;
        Uri? link = help && sendAs is Guid user ? await aids.PortalLinkAsync(d.CustomerId, user, cancellationToken).ConfigureAwait(false) : null;
        EmailAttachment? qr = help ? aids.QrCode(sender, invoice.Amount, d.Number) : null;
        ReminderHelp? extras = help
            ? new ReminderHelp(link, aids.OffersPortal, qr is not null, false, invoice.MissedInstallment, invoice.Progress?.Installments)
            : null;
        (string subject, string body) = ReminderTemplates.Write(new ReminderLetter(
            level, language, invoice.Customer.Name, d.Number, d.IssueDate, invoice.DueDate, invoice.Amount, invoice.DaysOverdue, invoice.Open.PenaltyRate,
            invoice.Penalty, sender, extras));
        ReminderDraft draft = new(d.InvoiceId, d.CustomerId, d.Number, level, language, subject, body, invoice.Amount, invoice.DaysOverdue,
            help ? invoice.MissedInstallment : null);
        return new PreparedReminder(draft, invoice.Customer, sender, qr is null ? null : [qr]);
    }

    /// <summary>Le rappel avant échéance, avec le lien de l'espace client et le QR code.</summary>
    private async Task<PreparedReminder> WriteAsync(UpcomingInvoice invoice, ReminderSender sender, ReminderSettings settings, Guid? sendAs, CancellationToken cancellationToken)
    {
        OpenInvoice open = invoice.Open;
        ReceivableDocument d = open.Document;
        string language = ReminderTemplates.LanguageOf(open.Customer.Language);
        Uri? link = sendAs is Guid user ? await aids.PortalLinkAsync(d.CustomerId, user, cancellationToken).ConfigureAwait(false) : null;
        EmailAttachment? qr = aids.QrCode(sender, d.Outstanding, d.Number);
        bool canRequestPlan = settings.PaymentPlansEnabled && open.Customer.IsProfessional && d.Outstanding >= settings.PaymentPlanMinimumAmount;
        (string subject, string body) = ReminderTemplates.Write(new ReminderLetter(
            ReminderLevel.BeforeDue, language, open.Customer.Name, d.Number, d.IssueDate, invoice.DueDate, d.Outstanding, 0, open.PenaltyRate, default, sender,
            new ReminderHelp(link, aids.OffersPortal, qr is not null, canRequestPlan && aids.OffersPortal)));
        ReminderDraft draft = new(d.InvoiceId, d.CustomerId, d.Number, ReminderLevel.BeforeDue, language, subject, body, d.Outstanding, 0);
        return new PreparedReminder(draft, open.Customer, sender, qr is null ? null : [qr]);
    }

    private static ReminderInvoiceResponse Describe(OverdueInvoice o, ReminderSettings settings, DateOnly today)
    {
        LatePenalty penalty = o.Penalty;
        PaymentReminder? last = o.Open.Track.Last;
        return new ReminderInvoiceResponse(
            o.Document.InvoiceId.Value,
            o.Document.Number,
            o.Document.CustomerId.Value,
            o.Customer.Name,
            o.Customer.Email,
            ReminderTemplates.LanguageOf(o.Customer.Language),
            o.Document.IssueDate,
            o.DueDate,
            o.Amount,
            o.DaysOverdue,
            (ReminderLevelDto?)last?.Level,
            last?.OccurredAt,
            (ReminderLevelDto?)o.NextLevel,
            o.DueToday(settings, today),
            penalty.Interest,
            penalty.RecoveryIndemnity,
            o.Open.Plan?.Id,
            o.MissedInstallment);
    }

    private static UpcomingReminderResponse Describe(UpcomingInvoice u, DateOnly today)
    {
        OpenInvoice o = u.Open;
        return new UpcomingReminderResponse(
            o.Document.InvoiceId.Value,
            o.Document.Number,
            o.Document.CustomerId.Value,
            o.Customer.Name,
            o.Customer.Email,
            ReminderTemplates.LanguageOf(o.Customer.Language),
            o.Document.IssueDate,
            u.DueDate,
            o.Document.Outstanding,
            u.Weather.Weather,
            u.Weather.BestSendDay,
            u.PlannedOn,
            u.DueToday(today),
            o.Track.BeforeDue?.OccurredAt);
    }
}

internal static class ReminderMapping
{
    public static ReminderPreventionDto ToDto(this ReminderPrevention p) =>
        new(p.BeforeDueEnabled, p.BeforeDueDays, p.PaymentPlansEnabled, p.PaymentPlanMaxInstallments, p.PaymentPlanMinimumAmount);

    public static ReminderPrevention ToDomain(this ReminderPreventionDto p) =>
        new(p.BeforeDueEnabled, p.BeforeDueDays, p.PaymentPlansEnabled, p.PaymentPlanMaxInstallments, p.PaymentPlanMinimumAmount);

    public static PaymentReminderResponse ToResponse(this PaymentReminder r, string customerName) => new(
        r.Id, r.InvoiceId.Value, r.InvoiceNumber, r.CustomerId.Value, customerName, (ReminderLevelDto)r.Level, (ReminderChannelDto)r.Channel,
        (ReminderOutcomeDto)r.Outcome, r.ErrorCode, r.Recipient, r.Language, r.Subject, r.Body, r.Outstanding, r.DaysOverdue,
        (ReminderTriggerDto)r.Trigger, r.OccurredAt);
}

internal sealed class GetReminderOverviewHandler(PaymentReminderService service) : IQueryHandler<GetReminderOverviewQuery, Result<ReminderOverviewResponse>>
{
    public async Task<Result<ReminderOverviewResponse>> HandleAsync(GetReminderOverviewQuery query, CancellationToken cancellationToken) =>
        await service.OverviewAsync(cancellationToken).ConfigureAwait(false);
}

internal sealed class GetReminderPreviewHandler(PaymentReminderService service) : IQueryHandler<GetReminderPreviewQuery, Result<ReminderPreviewResponse>>
{
    public async Task<Result<ReminderPreviewResponse>> HandleAsync(GetReminderPreviewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Result<PreparedReminder> drafted = await service.DraftAsync(new InvoiceId(query.InvoiceId), cancellationToken).ConfigureAwait(false);
        return drafted.Map(d => new ReminderPreviewResponse(
            d.Draft.InvoiceId.Value, (ReminderLevelDto)d.Draft.Level, d.Draft.Language, d.Customer.Email, d.Draft.Subject, d.Draft.Body));
    }
}

internal sealed class SendReminderHandler(PaymentReminderService service, IReminderContextReader context, ICurrentUser user)
    : ICommandHandler<SendReminderCommand, Result<PaymentReminderResponse>>
{
    public async Task<Result<PaymentReminderResponse>> HandleAsync(SendReminderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ReminderChannel channel = command.Request?.Channel == ReminderChannelDto.Manual ? ReminderChannel.Manual : ReminderChannel.Email;
        Result<PaymentReminder> sent = await service.SendAsync(new InvoiceId(command.InvoiceId), channel, ReminderTrigger.User, user.UserId, cancellationToken).ConfigureAwait(false);
        if (sent.IsFailure)
        {
            return Result<PaymentReminderResponse>.Failure(sent.Errors);
        }

        IReadOnlyDictionary<CustomerId, ReminderCustomer> customers = await context.CustomersAsync([sent.Value.CustomerId], cancellationToken).ConfigureAwait(false);
        return sent.Value.ToResponse(customers.GetValueOrDefault(sent.Value.CustomerId)?.Name ?? "");
    }
}

internal sealed class RunDueRemindersHandler(PaymentReminderService service, ICurrentUser user) : ICommandHandler<RunDueRemindersCommand, Result<RunRemindersResponse>>
{
    public Task<Result<RunRemindersResponse>> HandleAsync(RunDueRemindersCommand command, CancellationToken cancellationToken) =>
        service.RunDueAsync(ReminderTrigger.User, user.UserId, cancellationToken);
}

internal sealed class ListPaymentRemindersHandler(IPaymentReminderRepository reminders)
    : IQueryHandler<ListPaymentRemindersQuery, Result<PagedResponse<PaymentReminderResponse>>>
{
    public const int MaxPageSize = Paging.MaxPageSize;

    public async Task<Result<PagedResponse<PaymentReminderResponse>>> HandleAsync(ListPaymentRemindersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        int page = Math.Max(1, query.Page);
        int size = Math.Clamp(query.PageSize, 1, MaxPageSize);
        ReminderHistoryFilter filter = new(
            query.InvoiceId is Guid i ? new InvoiceId(i) : null,
            query.CustomerId is Guid c ? new CustomerId(c) : null,
            (ReminderLevel?)query.Level,
            (ReminderOutcome?)query.Outcome,
            (ReminderChannel?)query.Channel,
            query.From,
            query.To,
            ListSearch.Normalize(query.Search),
            query.Sort,
            query.Descending,
            page,
            size);
        (IReadOnlyList<ReminderHistoryItem> items, int total) = await reminders.ListAsync(filter, cancellationToken).ConfigureAwait(false);
        return new PagedResponse<PaymentReminderResponse>([.. items.Select(x => x.Reminder.ToResponse(x.CustomerName))], page, size, total);
    }
}

internal sealed class GetReminderSettingsHandler(PaymentReminderService service) : IQueryHandler<GetReminderSettingsQuery, Result<ReminderSettingsResponse>>
{
    public async Task<Result<ReminderSettingsResponse>> HandleAsync(GetReminderSettingsQuery query, CancellationToken cancellationToken) =>
        service.Describe(await service.SettingsAsync(cancellationToken).ConfigureAwait(false));
}

internal sealed class UpdateReminderSettingsHandler(
    IReminderSettingsRepository settings, PaymentReminderService service, IUnitOfWork unitOfWork, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<UpdateReminderSettingsCommand, Result<ReminderSettingsResponse>>
{
    public async Task<Result<ReminderSettingsResponse>> HandleAsync(UpdateReminderSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ReminderSettings? current = await settings.FindAsync(cancellationToken).ConfigureAwait(false);
        bool created = current is null;
        current ??= ReminderSettings.Default();
        UpdateReminderSettingsRequest request = command.Request ?? new UpdateReminderSettingsRequest();
        Result updated = current.Update(
            request.AutoSend, request.Delays ?? [], request.MinimumGapDays, request.Prevention?.ToDomain() ?? current.Prevention, user.UserId, clock.GetUtcNow());
        if (updated.IsFailure)
        {
            return Result<ReminderSettingsResponse>.Failure(updated.Errors);
        }

        if (created)
        {
            await settings.AddAsync(current, cancellationToken).ConfigureAwait(false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return service.Describe(current);
    }
}
