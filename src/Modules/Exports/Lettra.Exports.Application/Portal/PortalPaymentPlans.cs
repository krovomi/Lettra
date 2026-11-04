using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Portal;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.Portal;
using Lettra.Domain.Reminders;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Banking;
using Lettra.Exports.Application.Reminders;

namespace Lettra.Exports.Application.Portal;

/// <summary>
/// Demander un échéancier depuis l'espace client : Lettra le crée tout de suite s'il tient dans les bornes de
/// l'entreprise (les mêmes pour tous), puis l'alerte du dossier prévient l'entreprise.
/// </summary>
public sealed record RequestPaymentPlanOnPortalCommand(DossierId Dossier, string SecretHash, Guid InvoiceId, RequestPaymentPlanRequest Request)
    : ICommand<Result<PaymentPlanRequestResult>>, IRequirePermission
{
    public Permission Permission => Permission.PortailConsulter;
}

/// <summary>Ce que l'espace client montre des échéanciers d'une facture : celui en cours, ou ceux que le client peut demander.</summary>
public sealed record PortalPlanOffer(PortalPaymentPlan? Plan, IReadOnlyList<PortalPlanOption> Options);

/// <summary>Les échéanciers vus de l'espace client : les bornes de l'entreprise, la facture, le client.</summary>
public sealed class PortalPaymentPlans(IReminderSettingsRepository settings, IPaymentPlanRepository plans, IReminderContextReader context)
{
    /// <summary>Pour chaque facture encore due : son échéancier en cours, ou les échéanciers possibles.</summary>
    public async Task<IReadOnlyDictionary<InvoiceId, PortalPlanOffer>> OffersAsync(
        Customer customer, IReadOnlyCollection<ReceivableDocument> documents, DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(documents);
        List<ReceivableDocument> due = [.. documents.Where(d => d.Kind != InvoiceKind.CreditNote && d.Outstanding > 0m)];
        if (due.Count == 0)
        {
            return new Dictionary<InvoiceId, PortalPlanOffer>();
        }

        InvoiceId[] ids = [.. due.Select(d => d.InvoiceId)];
        ReminderPrevention bounds = await BoundsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<InvoiceId, PaymentPlan> active = await plans.ActiveAsync(ids, cancellationToken).ConfigureAwait(false);
        IReadOnlySet<InvoiceId> disputed = bounds.PaymentPlansEnabled
            ? await context.DisputedAsync(ids, cancellationToken).ConfigureAwait(false)
            : new HashSet<InvoiceId>();
        return due.ToDictionary(d => d.InvoiceId, d => active.TryGetValue(d.InvoiceId, out PaymentPlan? plan)
            ? new PortalPlanOffer(Describe(plan, d.Outstanding, today), [])
            : new PortalPlanOffer(null, [.. PaymentPlanRules.Options(Subject(d, customer, disputed.Contains(d.InvoiceId), false), bounds, today)
                .Select(schedule => new PortalPlanOption(schedule.Count, [.. schedule.Select(i => new PortalPlanInstallment(i.Number, i.DueDate, i.Amount, false))]))]));
    }

    /// <summary>Ce que l'échéancier doit savoir de la facture à la demande.</summary>
    public async Task<PaymentPlanSubject> SubjectAsync(Customer customer, ReceivableDocument invoice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(invoice);
        IReadOnlySet<InvoiceId> disputed = await context.DisputedAsync([invoice.InvoiceId], cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<InvoiceId, PaymentPlan> active = await plans.ActiveAsync([invoice.InvoiceId], cancellationToken).ConfigureAwait(false);
        return Subject(invoice, customer, disputed.Contains(invoice.InvoiceId), active.ContainsKey(invoice.InvoiceId));
    }

    public async Task<ReminderPrevention> BoundsAsync(CancellationToken cancellationToken) =>
        (await settings.FindAsync(cancellationToken).ConfigureAwait(false) ?? ReminderSettings.Default()).Prevention;

    /// <summary>Un échéancier tel que le client le lit : ses échéances, réglées ou non.</summary>
    public static PortalPaymentPlan Describe(PaymentPlan plan, decimal outstanding, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new PortalPaymentPlan(plan.RequestedBy, plan.RequestedAt,
        [
            .. plan.Progress(outstanding, today).Installments
                .Select(i => new PortalPlanInstallment(i.Installment.Number, i.Installment.DueDate, i.Installment.Amount, i.Paid)),
        ]);
    }

    private static PaymentPlanSubject Subject(ReceivableDocument d, Customer customer, bool disputed, bool planned) => new(
        d.InvoiceId, d.CustomerId, d.Number, d.Kind, d.IssueDate, d.Outstanding, customer.Kind == CustomerKind.Company, disputed, planned);
}

internal sealed class RequestPaymentPlanOnPortalHandler(
    ICustomerPortalSettings portal,
    IPortalLinkRepository links,
    ICustomerRepository customers,
    IReceivableDocumentReader receivables,
    IPaymentPlanRepository plans,
    PortalPaymentPlans offers,
    PaymentPlanAlerts alerts,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<RequestPaymentPlanOnPortalCommand, Result<PaymentPlanRequestResult>>
{
    public async Task<Result<PaymentPlanRequestResult>> HandleAsync(RequestPaymentPlanOnPortalCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<PortalLink> access = await PortalAccess.ActiveLinkAsync(portal, links, command.SecretHash, clock, cancellationToken).ConfigureAwait(false);
        if (access.IsFailure)
        {
            return Result<PaymentPlanRequestResult>.Failure(access.Errors);
        }

        PortalLink link = access.Value;
        Customer? customer = await customers.FindAsync(link.CustomerId, cancellationToken).ConfigureAwait(false);
        ReceivableDocument? invoice = (await receivables.ListAsync(link.CustomerId, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(d => d.InvoiceId.Value == command.InvoiceId && d.Kind != InvoiceKind.CreditNote);
        if (customer is null || invoice is null)
        {
            return PortalErrors.NothingToPay;
        }

        PaymentPlanSubject subject = await offers.SubjectAsync(customer, invoice, cancellationToken).ConfigureAwait(false);
        ReminderPrevention bounds = await offers.BoundsAsync(cancellationToken).ConfigureAwait(false);
        RequestPaymentPlanRequest request = command.Request ?? new RequestPaymentPlanRequest(0, "", false);
        DateOnly today = LegalCalendar.Today(clock);
        Result<PaymentPlan> created = PaymentPlan.Request(
            subject, bounds, request.Installments, request.RequesterName, request.Consent, link.Id, user.UserId, today, clock.GetUtcNow());
        if (created.IsFailure)
        {
            return Result<PaymentPlanRequestResult>.Failure(created.Errors);
        }

        await plans.AddAsync(created.Value, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await alerts.PublishAsync(cancellationToken).ConfigureAwait(false);
        return new PaymentPlanRequestResult(invoice.Number, PortalPaymentPlans.Describe(created.Value, invoice.Outstanding, today).Installments);
    }
}
