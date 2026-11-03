using System.Globalization;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Reminders;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.Reminders;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Banking;

namespace Lettra.Exports.Application.Reminders;

/// <summary>L'échéancier d'une facture (le dernier demandé, en cours ou arrêté), avec son suivi.</summary>
public sealed record GetInvoicePaymentPlanQuery(DossierId Dossier, Guid InvoiceId) : IQuery<Result<InvoicePaymentPlanResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>L'entreprise a vu l'échéancier demandé par son client : l'alerte du dossier se lève quand plus aucun n'attend.</summary>
public sealed record AcknowledgePaymentPlanCommand(DossierId Dossier, Guid PlanId, PaymentPlanDecisionRequest Request)
    : ICommand<Result<PaymentPlanResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureEmettre;
}

/// <summary>
/// L'entreprise met fin à l'échéancier (accord trouvé autrement, client qui ne s'y tient pas…) : la facture reprend ses
/// relances habituelles. Une décision humaine, jamais automatique.
/// </summary>
public sealed record CancelPaymentPlanCommand(DossierId Dossier, Guid PlanId, PaymentPlanDecisionRequest Request)
    : ICommand<Result<PaymentPlanResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureEmettre;
}

/// <summary>
/// Noter (ou retirer) l'opposition d'un client au ciblage selon son comportement de paiement : le rappel avant échéance
/// ne lui est plus proposé. C'est une donnée de la fiche client, d'où le droit de modifier les tiers.
/// </summary>
public sealed record SetTargetingObjectionCommand(DossierId Dossier, Guid CustomerId, SetTargetingObjectionRequest Request) : ICommand<Result>, IRequirePermission
{
    public Permission Permission => Permission.TiersModifier;
}

/// <summary>
/// Les signaux « Avant le retard » du jour, en lecture seule, pour « Décisions à prendre » : chacun a son type, ce dont
/// il s'agit, l'effet chiffré, l'échéance et l'écran qui montre le détail.
/// </summary>
public sealed record GetPaymentPreventionSignalsQuery(DossierId Dossier) : IQuery<Result<IReadOnlyList<PaymentPreventionSignal>>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>
/// Un signal « Avant le retard ». <paramref name="Kind"/> est stable (voir <see cref="PaymentPreventionSignals"/>) ;
/// <paramref name="Subject"/> est la facture ou l'échéancier ; <paramref name="Impact"/> le montant TTC en jeu ;
/// <paramref name="DueOn"/> avant quand ; <paramref name="Facts"/> les valeurs des phrases (restent dans la base du
/// dossier) ; <paramref name="Link"/> l'écran du détail.
/// </summary>
public sealed record PaymentPreventionSignal(
    string Kind, Guid Subject, decimal? Impact, DateOnly? DueOn, IReadOnlyDictionary<string, string> Facts, string Link);

/// <summary>Les types de signaux « Avant le retard » et leur construction.</summary>
public static class PaymentPreventionSignals
{
    /// <summary>Un rappel avant échéance est à envoyer aujourd'hui.</summary>
    public const string BeforeDueKind = "avant-le-retard.rappel";

    /// <summary>Un client a demandé un échéancier que l'entreprise n'a pas encore vu.</summary>
    public const string PlanToReviewKind = "avant-le-retard.echeancier-a-voir";

    /// <summary>Une échéance d'échéancier est manquée et sa relance est due.</summary>
    public const string MissedInstallmentKind = "avant-le-retard.echeance-manquee";

    public const string RemindersLink = "/reminders";

    internal static PaymentPreventionSignal BeforeDue(ReceivableDocument invoice, string customer, DateOnly dueDate) => new(
        BeforeDueKind, invoice.InvoiceId.Value, invoice.Outstanding, dueDate,
        new Dictionary<string, string>(StringComparer.Ordinal) { ["numero"] = invoice.Number, ["client"] = customer, ["echeance"] = dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
        RemindersLink);

    internal static PaymentPreventionSignal MissedInstallment(ReceivableDocument invoice, string customer, int installment, decimal late, DateOnly missedOn) => new(
        MissedInstallmentKind, invoice.InvoiceId.Value, late, missedOn,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["numero"] = invoice.Number,
            ["client"] = customer,
            ["rang"] = installment.ToString(CultureInfo.InvariantCulture),
        },
        RemindersLink);

    internal static PaymentPreventionSignal PlanToReview(PaymentPlan plan, string customer) => new(
        PlanToReviewKind, plan.Id, plan.Total, plan.Installments[0].DueDate,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["numero"] = plan.InvoiceNumber,
            ["client"] = customer,
            ["echeances"] = plan.Installments.Count.ToString(CultureInfo.InvariantCulture),
        },
        RemindersLink);
}

/// <summary>Un échéancier tel que l'entreprise le lit : son état (à jour, en retard, non tenu, soldé, arrêté) et ses échéances.</summary>
internal static class PaymentPlanViews
{
    public static PaymentPlanResponse Describe(PaymentPlan plan, ReceivableDocument? invoice, DateOnly today)
    {
        decimal outstanding = Math.Max(0m, invoice?.Outstanding ?? 0m);
        PaymentPlanProgress progress = plan.Progress(outstanding, today);
        PaymentPlanStatusDto status = plan.Status == PaymentPlanStatus.Cancelled ? PaymentPlanStatusDto.Cancelled
            : progress.Completed ? PaymentPlanStatusDto.Completed
            : progress.Lapsed ? PaymentPlanStatusDto.Lapsed
            : progress.LastMissed is not null ? PaymentPlanStatusDto.Late
            : PaymentPlanStatusDto.OnTrack;
        return new PaymentPlanResponse(
            plan.Id,
            plan.InvoiceId.Value,
            plan.InvoiceNumber,
            plan.CustomerId.Value,
            invoice?.CustomerName ?? "",
            status,
            plan.Total,
            outstanding,
            [.. progress.Installments.Select(i => new PaymentPlanInstallmentResponse(i.Installment.Number, i.Installment.DueDate, i.Installment.Amount, i.Paid, i.Missed, i.Remaining))],
            plan.RequestedBy,
            plan.RequestedAt,
            plan.IsAcknowledged,
            plan.CancelledAt,
            plan.Version);
    }
}

/// <summary>
/// L'alerte du dossier « Échéancier à voir » : levée tant qu'un échéancier demandé par un client n'a pas été vu par
/// l'entreprise, résolue quand plus aucun n'attend. Aucune donnée métier ne quitte la base du dossier.
/// </summary>
public sealed class PaymentPlanAlerts(IPaymentPlanRepository plans, ITenantContext tenant, IDossierAlertPublisher alerts, TimeProvider clock)
{
    public async Task PublishAsync(CancellationToken cancellationToken)
    {
        if (!tenant.IsResolved)
        {
            return;
        }

        int waiting = await plans.CountUnacknowledgedAsync(cancellationToken).ConfigureAwait(false);
        await alerts.PublishAsync(
            new DossierAlertSignal(DossierAlertRule.PaymentPlanToReview, tenant.Current.OrganisationId, tenant.Current.DossierId, waiting > 0, clock.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class GetInvoicePaymentPlanHandler(IPaymentPlanRepository plans, IReceivableDocumentReader receivables, TimeProvider clock)
    : IQueryHandler<GetInvoicePaymentPlanQuery, Result<InvoicePaymentPlanResponse>>
{
    public async Task<Result<InvoicePaymentPlanResponse>> HandleAsync(GetInvoicePaymentPlanQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        PaymentPlan? plan = await plans.LatestForInvoiceAsync(new InvoiceId(query.InvoiceId), cancellationToken).ConfigureAwait(false);
        if (plan is null)
        {
            return new InvoicePaymentPlanResponse(null);
        }

        ReceivableDocument? invoice = (await receivables.ListAsync(plan.CustomerId, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(d => d.InvoiceId == plan.InvoiceId);
        return new InvoicePaymentPlanResponse(PaymentPlanViews.Describe(plan, invoice, LegalCalendar.Today(clock)));
    }
}

/// <summary>Prendre en compte ou arrêter un échéancier : à la version lue, puis l'alerte du dossier suit.</summary>
public sealed class PaymentPlanDecisions(
    IPaymentPlanRepository plans,
    IReceivableDocumentReader receivables,
    PaymentPlanAlerts alerts,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    TimeProvider clock)
{
    internal async Task<Result<PaymentPlanResponse>> DecideAsync(
        Guid id, long version, Func<PaymentPlan, Guid, DateTimeOffset, Result> decide, CancellationToken cancellationToken)
    {
        Result<PaymentPlan> loaded = Concurrency.Load(await plans.FindAsync(id, cancellationToken).ConfigureAwait(false), PaymentPlanErrors.NotFound, version);
        if (loaded.IsFailure)
        {
            return Result<PaymentPlanResponse>.Failure(loaded.Errors);
        }

        PaymentPlan plan = loaded.Value;
        Result decided = decide(plan, user.UserId, clock.GetUtcNow());
        if (decided.IsFailure)
        {
            return Result<PaymentPlanResponse>.Failure(decided.Errors);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await alerts.PublishAsync(cancellationToken).ConfigureAwait(false);
        ReceivableDocument? invoice = (await receivables.ListAsync(plan.CustomerId, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(d => d.InvoiceId == plan.InvoiceId);
        return PaymentPlanViews.Describe(plan, invoice, LegalCalendar.Today(clock));
    }
}

internal sealed class AcknowledgePaymentPlanHandler(PaymentPlanDecisions decisions) : ICommandHandler<AcknowledgePaymentPlanCommand, Result<PaymentPlanResponse>>
{
    public Task<Result<PaymentPlanResponse>> HandleAsync(AcknowledgePaymentPlanCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return decisions.DecideAsync(command.PlanId, command.Request?.Version ?? -1, (plan, user, now) =>
        {
            plan.Acknowledge(user, now);
            return Result.Success();
        }, cancellationToken);
    }
}

internal sealed class CancelPaymentPlanHandler(PaymentPlanDecisions decisions) : ICommandHandler<CancelPaymentPlanCommand, Result<PaymentPlanResponse>>
{
    public Task<Result<PaymentPlanResponse>> HandleAsync(CancelPaymentPlanCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return decisions.DecideAsync(command.PlanId, command.Request?.Version ?? -1, (plan, user, now) => plan.Cancel(user, now), cancellationToken);
    }
}

internal sealed class SetTargetingObjectionHandler(
    IPaymentTargetingObjectionRepository objections,
    IReminderContextReader context,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<SetTargetingObjectionCommand, Result>
{
    public async Task<Result> HandleAsync(SetTargetingObjectionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        CustomerId customer = new(command.CustomerId);
        IReadOnlyDictionary<CustomerId, ReminderCustomer> known = await context.CustomersAsync([customer], cancellationToken).ConfigureAwait(false);
        if (!known.ContainsKey(customer))
        {
            return CustomerErrors.NotFound;
        }

        PaymentTargetingObjection? existing = await objections.FindAsync(customer, cancellationToken).ConfigureAwait(false);
        bool objects = command.Request?.Objects ?? true;
        if (objects && existing is null)
        {
            await objections.AddAsync(PaymentTargetingObjection.Record(customer, user.UserId, clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
        }
        else if (!objects && existing is not null)
        {
            await objections.RemoveAsync(existing, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            return Result.Success();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

internal sealed class GetPaymentPreventionSignalsHandler(PaymentReminderService service)
    : IQueryHandler<GetPaymentPreventionSignalsQuery, Result<IReadOnlyList<PaymentPreventionSignal>>>
{
    public async Task<Result<IReadOnlyList<PaymentPreventionSignal>>> HandleAsync(GetPaymentPreventionSignalsQuery query, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<PaymentPreventionSignal>>.Success(await service.SignalsAsync(cancellationToken).ConfigureAwait(false));
}
