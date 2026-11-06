using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Decisions;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Decisions;
using Lettra.Domain.Common;
using Lettra.Domain.Decisions;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Decisions;

/// <summary>Les décisions du dossier que l'utilisateur peut traiter : ouvertes d'abord, les plus urgentes en tête.</summary>
public sealed record ListDecisionsQuery(DossierId Dossier) : IQuery<Result<DecisionsResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DecisionsTraiter;
}

/// <summary>Faire tourner les règles maintenant (le bouton « Actualiser », ou l'écran ouvert sur des décisions anciennes).</summary>
public sealed record RefreshDecisionsCommand(DossierId Dossier) : ICommand<Result>, IRequirePermission
{
    public Permission Permission => Permission.DecisionsTraiter;
}

/// <summary>
/// Répondre à une décision : l'accepter (son action est faite avec les droits de l'utilisateur), la remettre à plus tard,
/// l'ignorer, ou la remettre dans la liste. Il faut aussi la permission de sa règle.
/// </summary>
public sealed record AnswerDecisionCommand(DossierId Dossier, DecisionId DecisionId, DecisionChoiceDto Choice, DateOnly? SnoozeUntil, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IVersionedCommand
{
    public Permission Permission => Permission.DecisionsTraiter;
}

/// <summary>Régler le cadran d'autonomie du dossier.</summary>
public sealed record SetDecisionPolicyCommand(DossierId Dossier, AutonomyLevelDto Level, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IVersionedCommand
{
    public Permission Permission => Permission.DecisionsRegler;
}

internal sealed class DecisionHandlers(
    IDecisionStore store,
    IEnumerable<IDecisionRule> rules,
    IEnumerable<IDecisionActionHandler> actions,
    IPermissionService permissions,
    ICurrentUser user,
    DecisionRefresher refresher,
    IUnitOfWork unitOfWork,
    TimeProvider clock) :
    IQueryHandler<ListDecisionsQuery, Result<DecisionsResponse>>,
    ICommandHandler<RefreshDecisionsCommand, Result>,
    ICommandHandler<AnswerDecisionCommand, Result>,
    ICommandHandler<SetDecisionPolicyCommand, Result>
{
    /// <summary>La liste montre aussi ce qui a été traité ces trente derniers jours.</summary>
    public const int HistoryDays = 30;

    public async Task<Result<DecisionsResponse>> HandleAsync(ListDecisionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        DateTimeOffset now = clock.GetUtcNow();
        IReadOnlySet<Permission> granted = await permissions.GetEffectiveAsync(user.UserId, query.Dossier, cancellationToken).ConfigureAwait(false);
        HashSet<string> visible = [.. rules.Where(r => granted.Contains(r.Permission)).Select(r => r.Kind)];
        HashSet<string> handled = [.. actions.Select(a => a.ActionKind)];
        DecisionPolicy? policy = await store.FindPolicyAsync(cancellationToken).ConfigureAwait(false);
        AutonomyLevel level = policy?.Level ?? AutonomyLevel.Prepare;
        IReadOnlyList<Decision> decisions = await store.ListAsync(now.AddDays(-HistoryDays), cancellationToken).ConfigureAwait(false);
        return DecisionView.Build(
            [.. decisions.Where(d => visible.Contains(d.Kind))],
            LegalCalendar.DateOf(now),
            d => level == AutonomyLevel.Prepare && d.ActionKind is { } kind && handled.Contains(kind),
            policy,
            granted.Contains(Permission.DecisionsRegler));
    }

    public async Task<Result> HandleAsync(RefreshDecisionsCommand command, CancellationToken cancellationToken)
    {
        await refresher.RefreshAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> HandleAsync(AnswerDecisionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<Decision> loaded = Concurrency.Load(
            await store.FindAsync(command.DecisionId, cancellationToken).ConfigureAwait(false), DecisionErrors.NotFound, command.ExpectedVersion);
        if (loaded.IsFailure)
        {
            return Result.Failure(loaded.Errors);
        }

        Decision decision = loaded.Value;
        IReadOnlySet<Permission> granted = await permissions.GetEffectiveAsync(user.UserId, command.Dossier, cancellationToken).ConfigureAwait(false);
        if (rules.FirstOrDefault(r => string.Equals(r.Kind, decision.Kind, StringComparison.Ordinal)) is not { } rule || !granted.Contains(rule.Permission))
        {
            // Une décision qu'on ne voit pas n'existe pas pour soi.
            return DecisionErrors.NotFound;
        }

        DateTimeOffset now = clock.GetUtcNow();
        Result answered = command.Choice switch
        {
            DecisionChoiceDto.Accept => await AcceptAsync(decision, command.Dossier, now, cancellationToken).ConfigureAwait(false),
            DecisionChoiceDto.Snooze => command.SnoozeUntil is { } until
                ? decision.Snooze(until, LegalCalendar.DateOf(now), user.UserId)
                : DecisionErrors.SnoozeDateInvalid,
            DecisionChoiceDto.Dismiss => decision.Dismiss(user.UserId, now),
            DecisionChoiceDto.Restore => decision.Restore(),
            _ => DecisionErrors.ChoiceInvalid,
        };
        if (answered.IsFailure)
        {
            return answered;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> HandleAsync(SetDecisionPolicyCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        DecisionPolicy? policy = await store.FindPolicyAsync(cancellationToken).ConfigureAwait(false);
        if (policy is null)
        {
            policy = DecisionPolicy.Default();
            await store.AddPolicyAsync(policy, cancellationToken).ConfigureAwait(false);
        }

        Result set = Concurrency.Check(policy, command.ExpectedVersion);
        if (set.IsSuccess)
        {
            set = policy.SetLevel((AutonomyLevel)(int)command.Level, user.UserId, clock.GetUtcNow());
        }

        if (set.IsFailure)
        {
            return set;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    /// <summary>
    /// Fait l'action préparée par sa commande existante (le pipeline vérifie la permission de celle-ci), puis note la
    /// décision acceptée. En échec, la décision reste ouverte et l'erreur de la commande remonte telle quelle.
    /// </summary>
    private async Task<Result> AcceptAsync(Decision decision, DossierId dossier, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!decision.IsOpen)
        {
            return DecisionErrors.NotOpen;
        }

        DecisionPolicy? policy = await store.FindPolicyAsync(cancellationToken).ConfigureAwait(false);
        if (decision.ActionKind is not { } kind || (policy?.Level ?? AutonomyLevel.Prepare) != AutonomyLevel.Prepare)
        {
            return DecisionErrors.NoAction;
        }

        if (actions.FirstOrDefault(a => string.Equals(a.ActionKind, kind, StringComparison.Ordinal)) is not { } action)
        {
            return DecisionErrors.ActionUnavailable;
        }

        Result done = await action.ExecuteAsync(dossier, decision.ActionPayload ?? string.Empty, cancellationToken).ConfigureAwait(false);
        return done.IsFailure ? done : decision.Accept(user.UserId, now);
    }
}

/// <summary>Les calculs de l'écran « Décisions à prendre » et de son indicateur, en fonctions pures.</summary>
public static class DecisionView
{
    public static DecisionsResponse Build(
        IReadOnlyList<Decision> decisions, DateOnly today, Func<Decision, bool> canAccept, DecisionPolicy? policy, bool canConfigure)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(canAccept);
        List<DecisionRow> rows =
        [
            .. decisions
                .OrderBy(d => Rank(d.StatusOn(today)))
                .ThenByDescending(d => d.Urgency)
                .ThenBy(d => d.DueOn ?? DateOnly.MaxValue)
                .ThenByDescending(d => d.ClosedAt)
                .ThenBy(d => d.ProposedAt)
                .Select(d => ToRow(d, today, d.IsOpen && canAccept(d))),
        ];
        return new DecisionsResponse(
            rows, (AutonomyLevelDto)(int)(policy?.Level ?? AutonomyLevel.Prepare), policy?.Version, policy?.RefreshedAt, canConfigure);
    }

    public static DecisionRow ToRow(Decision decision, DateOnly today, bool canAccept)
    {
        ArgumentNullException.ThrowIfNull(decision);
        DecisionStatus status = decision.StatusOn(today);
        return new DecisionRow(
            decision.Id.Value, decision.Kind, (DecisionAudienceDto)(int)decision.Audience, (DecisionUrgencyDto)(int)decision.Urgency, decision.Impact,
            decision.DueOn, decision.Facts, decision.Link, canAccept, (DecisionStatusDto)(int)status,
            status == DecisionStatus.Snoozed ? decision.SnoozedUntil : null, decision.ProposedAt, decision.ClosedAt, decision.Version);
    }

    /// <summary>
    /// L'indicateur de la vue cabinet : les décisions à décider (pas celles remises à plus tard), au niveau de la plus
    /// urgente, échéance = la plus proche. Le compteur seulement : ni nom ni montant ; sans les décisions qui révèlent le
    /// niveau d'attention du dossier (<see cref="DecisionKinds.CabinetOnly"/>).
    /// </summary>
    public static DossierIndicator? Indicator(IReadOnlyList<Decision> decisions, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        List<Decision> pending = [.. decisions.Where(d => d.StatusOn(today) == DecisionStatus.Proposed && !DecisionKinds.CabinetOnly.Contains(d.Kind))];
        if (pending.Count == 0)
        {
            return null;
        }

        IndicatorLevel level = pending.Max(d => d.Urgency) switch
        {
            DecisionUrgency.Critical => IndicatorLevel.Critical,
            DecisionUrgency.High => IndicatorLevel.Warning,
            _ => IndicatorLevel.Info,
        };
        return new DossierIndicator(DossierIndicatorKeys.DecisionsPending, pending.Count, level, pending.Min(d => d.DueOn));
    }

    /// <summary>À décider d'abord, puis plus tard, puis ce qui est traité.</summary>
    private static int Rank(DecisionStatus status) => status switch
    {
        DecisionStatus.Proposed => 0,
        DecisionStatus.Snoozed => 1,
        _ => 2,
    };
}

/// <summary>
/// La passe quotidienne de la vue cabinet actualise aussi les décisions de chaque dossier, puis compte celles à décider :
/// une seule passe sur les dossiers, sans tâche de plus.
/// </summary>
internal sealed class DecisionsIndicatorSource(DecisionRefresher refresher, IDecisionStore store, TimeProvider clock) : IDossierIndicatorSource
{
    public async Task<IReadOnlyList<DossierIndicator>> ComputeAsync(DateOnly today, CancellationToken cancellationToken)
    {
        await refresher.RefreshAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<Decision> open = await store.ListAsync(clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return DecisionView.Indicator(open, today) is { } indicator ? [indicator] : [];
    }
}
