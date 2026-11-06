using Lettra.Application.Abstractions.Decisions;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Features.Decisions;
using Lettra.Domain.Decisions;

namespace Lettra.Exports.Application.Reminders;

/// <summary>
/// « Avant le retard » dans « Décisions à prendre » : chaque signal du jour (rappel avant échéance à envoyer, échéancier
/// demandé à voir, échéance manquée à relancer) devient une décision de son type. Le sujet porte l'échéance : une
/// nouvelle échéance manquée de la même facture est une nouvelle décision. Pas d'action préparée : la décision mène à
/// l'écran des relances. Rien n'est proposé pour un envoi que la passe quotidienne fait seule (envoi automatique réglé).
/// </summary>
internal abstract class PaymentPreventionDecisionRule(TodaysPaymentPreventionSignals signals) : IDecisionRule
{
    public abstract string Kind { get; }

    public int Version => 1;

    public Permission Permission => Permission.FactureEmettre;

    /// <summary>Normale, ou urgente pour une échéance déjà manquée.</summary>
    protected abstract DecisionUrgency Urgency { get; }

    /// <summary>Vrai quand la passe quotidienne envoie elle-même ce que la décision proposerait.</summary>
    protected virtual bool SentByDailyPass => true;

    public async Task<IReadOnlyList<DecisionProposal>> ProposeAsync(DateOnly today, CancellationToken cancellationToken)
    {
        (IReadOnlyList<PaymentPreventionSignal> found, bool autoSend) = await signals.ReadAsync(cancellationToken).ConfigureAwait(false);
        return autoSend && SentByDailyPass ? [] : Proposals(Kind, Urgency, found);
    }

    /// <summary>Les signaux d'un type, en fonction pure : une décision par sujet et par échéance.</summary>
    public static IReadOnlyList<DecisionProposal> Proposals(string kind, DecisionUrgency urgency, IEnumerable<PaymentPreventionSignal> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        return
        [
            .. signals.Where(s => string.Equals(s.Kind, kind, StringComparison.Ordinal)).Select(s => new DecisionProposal(
                kind,
                s.DueOn is { } due ? $"{s.Subject}:{DecisionFacts.Date(due)}" : s.Subject.ToString(),
                DecisionAudience.Entreprise,
                urgency,
                s.Impact,
                s.DueOn,
                s.Facts,
                s.Link.TrimStart('/'))),
        ];
    }
}

/// <summary>Un rappel avant échéance est à envoyer aujourd'hui à un client ciblé par la météo des paiements.</summary>
internal sealed class BeforeDueDecisionRule(TodaysPaymentPreventionSignals signals) : PaymentPreventionDecisionRule(signals)
{
    public override string Kind => PaymentPreventionSignals.BeforeDueKind;

    protected override DecisionUrgency Urgency => DecisionUrgency.Normal;
}

/// <summary>Un client a demandé un échéancier que l'entreprise n'a pas encore vu : à prendre en compte ou à arrêter.</summary>
internal sealed class PaymentPlanToReviewDecisionRule(TodaysPaymentPreventionSignals signals) : PaymentPreventionDecisionRule(signals)
{
    public override string Kind => PaymentPreventionSignals.PlanToReviewKind;

    protected override DecisionUrgency Urgency => DecisionUrgency.Normal;

    protected override bool SentByDailyPass => false;
}

/// <summary>Une échéance d'échéancier n'est pas payée et sa relance est due aujourd'hui.</summary>
internal sealed class MissedInstallmentDecisionRule(TodaysPaymentPreventionSignals signals) : PaymentPreventionDecisionRule(signals)
{
    public override string Kind => PaymentPreventionSignals.MissedInstallmentKind;

    protected override DecisionUrgency Urgency => DecisionUrgency.High;
}

/// <summary>
/// Les signaux « Avant le retard » lus une fois par actualisation : les trois règles les partagent dans la portée du
/// dossier, avec le réglage d'envoi automatique.
/// </summary>
internal sealed class TodaysPaymentPreventionSignals(PaymentReminderService reminders)
{
    private Task<(IReadOnlyList<PaymentPreventionSignal> Signals, bool AutoSend)>? _read;

    public Task<(IReadOnlyList<PaymentPreventionSignal> Signals, bool AutoSend)> ReadAsync(CancellationToken cancellationToken) =>
        _read ??= ReadOnceAsync(cancellationToken);

    private async Task<(IReadOnlyList<PaymentPreventionSignal>, bool)> ReadOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<PaymentPreventionSignal> found = await reminders.SignalsAsync(cancellationToken).ConfigureAwait(false);
        bool autoSend = (await reminders.SettingsAsync(cancellationToken).ConfigureAwait(false)).AutoSend;
        return (found, autoSend);
    }
}
