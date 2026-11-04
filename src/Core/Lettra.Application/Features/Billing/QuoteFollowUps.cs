using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Mail;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Billing;

/// <summary>
/// Les devis envoyés sans réponse dont la fin de validité approche (fenêtre réglée, 5 jours par défaut), ceux à relancer
/// d'abord. C'est aussi la requête des constats que « Décisions à prendre » reprend : raison (jours restants, jours
/// sans réponse), effet chiffré (montant et montant pondéré) et échéance (fin de validité).
/// </summary>
public sealed record GetQuoteFollowUpOverviewQuery(DossierId Dossier) : IQuery<Result<QuoteFollowUpOverviewResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>La relance proposée pour un devis en attente, dans la langue du client, à relire avant l'envoi.</summary>
public sealed record GetQuoteFollowUpPreviewQuery(DossierId Dossier, QuoteId QuoteId) : IQuery<Result<QuoteFollowUpPreviewResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>
/// Relancer un devis en attente par e-mail, ou noter une relance faite autrement (téléphone, rendez-vous). Un envoi en
/// échec est gardé dans l'historique et répond <c>Email.SendFailed</c>.
/// </summary>
public sealed record SendQuoteFollowUpCommand(DossierId Dossier, QuoteId QuoteId, SendQuoteFollowUpRequest Request)
    : ICommand<Result<QuoteFollowUpResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DevisCreer;
}

/// <summary>L'historique des relances de devis, filtré et trié, page par page.</summary>
public sealed record ListQuoteFollowUpsQuery(
    DossierId Dossier,
    Guid? QuoteId = null,
    Guid? CustomerId = null,
    QuoteFollowUpChannelDto? Channel = null,
    QuoteFollowUpOutcomeDto? Outcome = null,
    DateOnly? From = null,
    DateOnly? To = null,
    string? Search = null,
    QuoteFollowUpSortDto Sort = QuoteFollowUpSortDto.Date,
    bool Descending = true,
    int Page = 1,
    int PageSize = 50)
    : IQuery<Result<PagedResponse<QuoteFollowUpResponse>>>, IRequirePermission
{
    public const int MaxPageSize = Paging.MaxPageSize;
    public const int MaxSearchLength = 100;

    public Permission Permission => Permission.DossierLire;
}

/// <summary>Régler la fenêtre des devis à relancer : même droit que le scénario des relances de factures.</summary>
public sealed record UpdateQuoteFollowUpSettingsCommand(DossierId Dossier, UpdateQuoteFollowUpSettingsRequest Request)
    : ICommand<Result<QuoteFollowUpSettingsResponse>>, IRequirePermission
{
    public Permission Permission => Permission.CatalogueGerer;
}

/// <summary>Une relance de devis écrite, avec le devis, le client et le signataire.</summary>
public sealed record PreparedQuoteFollowUp(Quote Quote, QuoteCustomerContact Customer, string SenderName, string Language, string Subject, string Body, int DaysLeft);

/// <summary>
/// Les relances de devis d'un dossier : quels devis approchent de leur fin sans réponse, le texte proposé dans la langue
/// du client, l'envoi par le port e-mail (MailPit dans la démo) et la trace de chaque relance. Rien ne part sans un geste
/// de l'utilisateur : il n'y a pas d'envoi automatique.
/// </summary>
public sealed class QuoteFollowUpService(
    IQuoteRepository quotes,
    IQuotePipelineReader pipeline,
    IQuoteFollowUpRepository followUps,
    IQuoteFollowUpSettingsRepository settingsStore,
    ICompanyProfileRepository profiles,
    IEmailSender email,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public DateOnly Today => LegalCalendar.Today(clock);

    public bool EmailConfigured => email.IsConfigured;

    public async Task<QuoteFollowUpSettings> SettingsAsync(CancellationToken cancellationToken) =>
        await settingsStore.FindAsync(cancellationToken).ConfigureAwait(false) ?? QuoteFollowUpSettings.Default();

    public QuoteFollowUpSettingsResponse Describe(QuoteFollowUpSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new QuoteFollowUpSettingsResponse(settings.WindowDays, email.IsConfigured, settings.Version);
    }

    public async Task<QuoteFollowUpOverviewResponse> OverviewAsync(CancellationToken cancellationToken)
    {
        QuoteFollowUpSettings settings = await SettingsAsync(cancellationToken).ConfigureAwait(false);
        DateOnly today = Today;
        IReadOnlyList<QuoteFact> facts = await pipeline.ListAsync(OrderBookCalculator.HistoryFrom(today), cancellationToken).ConfigureAwait(false);
        Dictionary<QuoteId, WeightedQuote> weighted = OrderBookCalculator.Estimate(facts, today).Quotes.ToDictionary(w => w.Quote.Id);
        QuoteFact[] window =
        [
            .. facts.Where(q => q.Status == QuoteStatus.Validated && QuoteFollowUpPolicy.IsInWindow(q.ValidUntil, today, settings.WindowDays)),
        ];
        IReadOnlyDictionary<QuoteId, QuoteFollowUpStats> stats = window.Length == 0
            ? new Dictionary<QuoteId, QuoteFollowUpStats>()
            : await followUps.StatsAsync([.. window.Select(q => q.Id)], cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<CustomerId, QuoteCustomerContact> contacts = window.Length == 0
            ? new Dictionary<CustomerId, QuoteCustomerContact>()
            : await pipeline.ContactsAsync([.. window.Select(q => q.CustomerId).Distinct()], cancellationToken).ConfigureAwait(false);

        List<QuoteToFollowUpResponse> rows =
        [
            .. window
                .Select(q =>
                {
                    QuoteFollowUpStats? done = stats.GetValueOrDefault(q.Id);
                    QuoteCustomerContact contact = contacts.GetValueOrDefault(q.CustomerId) ?? new QuoteCustomerContact(null, null);
                    WeightedQuote? w = weighted.GetValueOrDefault(q.Id);
                    return new QuoteToFollowUpResponse(
                        q.Id.Value,
                        q.Number,
                        q.CustomerId.Value,
                        q.CustomerName,
                        contact.Email,
                        QuoteFollowUpTemplates.LanguageOf(contact.Language),
                        q.IssueDate,
                        q.ValidUntil!.Value,
                        QuoteFollowUpPolicy.DaysLeft(q.ValidUntil, today)!.Value,
                        Math.Max(0, today.DayNumber - q.IssueDate.DayNumber),
                        q.AmountExclTax,
                        q.AmountInclTax,
                        w?.Probability ?? 0m,
                        w?.WeightedExclTax ?? 0m,
                        QuoteFollowUpPolicy.IsDue(q.Status, q.ValidUntil, today, settings.WindowDays, done?.Last.OccurredOn),
                        done?.Count ?? 0,
                        done?.Last.OccurredAt,
                        (QuoteFollowUpChannelDto?)done?.Last.Channel);
                })
                .OrderByDescending(r => r.Due)
                .ThenBy(r => r.DaysLeft)
                .ThenByDescending(r => r.TotalExclTax)
                .ThenBy(r => r.Number, StringComparer.Ordinal),
        ];
        QuoteToFollowUpResponse[] due = [.. rows.Where(r => r.Due)];
        return new QuoteFollowUpOverviewResponse(rows, Describe(settings), due.Length, due.Sum(r => r.TotalExclTax));
    }

    /// <summary>La relance proposée pour un devis en attente, écrite dans la langue du client.</summary>
    public async Task<Result<PreparedQuoteFollowUp>> DraftAsync(QuoteId id, CancellationToken cancellationToken)
    {
        Quote? quote = await quotes.FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (quote is null)
        {
            return QuoteErrors.NotFound;
        }

        DateOnly today = Today;
        if (!quote.IsPending(today) || quote.Number is null || quote.IssueDate is null || quote.ValidUntil is null)
        {
            return QuoteFollowUpErrors.QuoteNotPending;
        }

        CompanyProfile? profile = await profiles.FindAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return QuoteFollowUpErrors.CompanyProfileMissing;
        }

        IReadOnlyDictionary<CustomerId, QuoteCustomerContact> contacts = await pipeline.ContactsAsync([quote.CustomerId], cancellationToken).ConfigureAwait(false);
        QuoteCustomerContact contact = contacts.GetValueOrDefault(quote.CustomerId) ?? new QuoteCustomerContact(null, null);
        string language = QuoteFollowUpTemplates.LanguageOf(contact.Language);
        int daysLeft = QuoteFollowUpPolicy.DaysLeft(quote.ValidUntil, today)!.Value;
        (string subject, string body) = QuoteFollowUpTemplates.Write(new QuoteFollowUpLetter(
            language, quote.Number, quote.IssueDate.Value, quote.ValidUntil.Value, daysLeft, quote.Subject, quote.TotalExclTax, quote.TotalInclTax, profile.Name));
        return new PreparedQuoteFollowUp(quote, contact, profile.Name, language, subject, body, daysLeft);
    }

    /// <summary>
    /// Envoie (ou note) la relance d'un devis et la garde dans l'historique, avec l'objet et le texte relus par
    /// l'utilisateur s'il en donne. Un e-mail refusé est gardé comme échec et répond <c>Email.SendFailed</c> ; un client
    /// sans adresse, ou une installation sans envoi d'e-mails, ne laisse aucune trace : rien n'a été tenté.
    /// </summary>
    public async Task<Result<QuoteFollowUpHistoryItem>> SendAsync(
        QuoteId id, QuoteFollowUpChannel channel, string? subject, string? body, Guid userId, CancellationToken cancellationToken)
    {
        Result<PreparedQuoteFollowUp> drafted = await DraftAsync(id, cancellationToken).ConfigureAwait(false);
        if (drafted.IsFailure)
        {
            return Result<QuoteFollowUpHistoryItem>.Failure(drafted.Errors);
        }

        PreparedQuoteFollowUp prepared = drafted.Value;
        string finalSubject = subject ?? prepared.Subject;
        string finalBody = body ?? prepared.Body;
        Result message = QuoteFollowUp.CheckMessage(finalSubject, finalBody);
        if (message.IsFailure)
        {
            return Result<QuoteFollowUpHistoryItem>.Failure(message.Errors);
        }

        Quote quote = prepared.Quote;
        QuoteFollowUpDraft draft = new(
            quote.Id, quote.CustomerId, quote.Number!, prepared.Language, finalSubject.Trim(), finalBody.Trim(), quote.TotalExclTax, quote.ValidUntil!.Value);
        string? recipient = null;
        string? errorCode = null;
        if (channel == QuoteFollowUpChannel.Email)
        {
            if (prepared.Customer.Email is null)
            {
                return QuoteFollowUpErrors.CustomerEmailMissing;
            }

            if (!email.IsConfigured)
            {
                return EmailErrors.NotConfigured;
            }

            Result<Email> to = Email.Create(prepared.Customer.Email);
            if (to.IsFailure)
            {
                return QuoteFollowUpErrors.CustomerEmailMissing;
            }

            recipient = to.Value.Value;
            Result sent = await email.SendAsync(new EmailMessage(to.Value, draft.Subject, draft.Body, SenderName: prepared.SenderName), cancellationToken).ConfigureAwait(false);
            errorCode = sent.IsFailure ? sent.Errors[0].Code : null;
        }

        QuoteFollowUp followUp = QuoteFollowUp.Record(draft, channel, recipient, errorCode, userId, clock.GetUtcNow());
        await followUps.AddAsync(followUp, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return errorCode is null ? new QuoteFollowUpHistoryItem(followUp, quote.Customer.Name) : EmailErrors.SendFailed;
    }
}

internal static class QuoteFollowUpMapping
{
    public static QuoteFollowUpResponse ToResponse(this QuoteFollowUp f, string customerName) => new(
        f.Id, f.QuoteId.Value, f.QuoteNumber, f.CustomerId.Value, customerName, (QuoteFollowUpChannelDto)f.Channel, (QuoteFollowUpOutcomeDto)f.Outcome,
        f.ErrorCode, f.Recipient, f.Language, f.Subject, f.Body, f.AmountExclTax, f.ValidUntil, f.OccurredAt);
}

internal sealed class GetQuoteFollowUpOverviewHandler(QuoteFollowUpService service)
    : IQueryHandler<GetQuoteFollowUpOverviewQuery, Result<QuoteFollowUpOverviewResponse>>
{
    public async Task<Result<QuoteFollowUpOverviewResponse>> HandleAsync(GetQuoteFollowUpOverviewQuery query, CancellationToken cancellationToken) =>
        await service.OverviewAsync(cancellationToken).ConfigureAwait(false);
}

internal sealed class GetQuoteFollowUpPreviewHandler(QuoteFollowUpService service)
    : IQueryHandler<GetQuoteFollowUpPreviewQuery, Result<QuoteFollowUpPreviewResponse>>
{
    public async Task<Result<QuoteFollowUpPreviewResponse>> HandleAsync(GetQuoteFollowUpPreviewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Result<PreparedQuoteFollowUp> drafted = await service.DraftAsync(query.QuoteId, cancellationToken).ConfigureAwait(false);
        return drafted.Map(d => new QuoteFollowUpPreviewResponse(
            d.Quote.Id.Value, d.Quote.Number!, d.Quote.Customer.Name, d.Customer.Email, d.Language, d.Subject, d.Body, d.DaysLeft, service.EmailConfigured));
    }
}

internal sealed class SendQuoteFollowUpHandler(QuoteFollowUpService service, ICurrentUser user)
    : ICommandHandler<SendQuoteFollowUpCommand, Result<QuoteFollowUpResponse>>
{
    public async Task<Result<QuoteFollowUpResponse>> HandleAsync(SendQuoteFollowUpCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        SendQuoteFollowUpRequest request = command.Request ?? new SendQuoteFollowUpRequest();
        QuoteFollowUpChannel channel = request.Channel == QuoteFollowUpChannelDto.Manual ? QuoteFollowUpChannel.Manual : QuoteFollowUpChannel.Email;
        Result<QuoteFollowUpHistoryItem> sent = await service
            .SendAsync(command.QuoteId, channel, request.Subject, request.Body, user.UserId, cancellationToken).ConfigureAwait(false);
        return sent.Map(s => s.FollowUp.ToResponse(s.CustomerName));
    }
}

internal sealed class ListQuoteFollowUpsValidator : IValidator<ListQuoteFollowUpsQuery>
{
    public IReadOnlyList<Error> Validate(ListQuoteFollowUpsQuery instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        List<Error> errors = [.. Paging.Validate(instance.Page, instance.PageSize, ListQuoteFollowUpsQuery.MaxPageSize)];
        if (instance.Search is { Length: > ListQuoteFollowUpsQuery.MaxSearchLength })
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.TooLong, nameof(instance.Search)));
        }

        return errors;
    }
}

internal sealed class ListQuoteFollowUpsHandler(IQuoteFollowUpRepository followUps)
    : IQueryHandler<ListQuoteFollowUpsQuery, Result<PagedResponse<QuoteFollowUpResponse>>>
{
    public async Task<Result<PagedResponse<QuoteFollowUpResponse>>> HandleAsync(ListQuoteFollowUpsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        QuoteFollowUpHistoryFilter filter = new(
            query.QuoteId is Guid q ? new QuoteId(q) : null,
            query.CustomerId is Guid c ? new CustomerId(c) : null,
            (QuoteFollowUpChannel?)query.Channel,
            (QuoteFollowUpOutcome?)query.Outcome,
            query.From,
            query.To,
            ListSearch.Normalize(query.Search),
            query.Sort,
            query.Descending,
            query.Page,
            query.PageSize);
        (IReadOnlyList<QuoteFollowUpHistoryItem> items, int total) = await followUps.ListAsync(filter, cancellationToken).ConfigureAwait(false);
        return new PagedResponse<QuoteFollowUpResponse>([.. items.Select(x => x.FollowUp.ToResponse(x.CustomerName))], query.Page, query.PageSize, total);
    }
}

internal sealed class UpdateQuoteFollowUpSettingsHandler(
    IQuoteFollowUpSettingsRepository settings, QuoteFollowUpService service, IUnitOfWork unitOfWork, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<UpdateQuoteFollowUpSettingsCommand, Result<QuoteFollowUpSettingsResponse>>
{
    public async Task<Result<QuoteFollowUpSettingsResponse>> HandleAsync(UpdateQuoteFollowUpSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        QuoteFollowUpSettings? current = await settings.FindAsync(cancellationToken).ConfigureAwait(false);
        bool created = current is null;
        current ??= QuoteFollowUpSettings.Default();
        UpdateQuoteFollowUpSettingsRequest request = command.Request ?? new UpdateQuoteFollowUpSettingsRequest();
        Result updated = current.Update(request.WindowDays, user.UserId, clock.GetUtcNow());
        if (updated.IsFailure)
        {
            return Result<QuoteFollowUpSettingsResponse>.Failure(updated.Errors);
        }

        if (created)
        {
            await settings.AddAsync(current, cancellationToken).ConfigureAwait(false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return service.Describe(current);
    }
}
