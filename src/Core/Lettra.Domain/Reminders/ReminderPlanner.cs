namespace Lettra.Domain.Reminders;

/// <summary>Ce que le planificateur sait d'une facture due : son retard et sa dernière relance faite.</summary>
/// <param name="DaysOverdue">Jours depuis l'échéance (0 si elle n'est pas passée).</param>
/// <param name="LastLevel">Niveau de la dernière relance partie ou notée ; un envoi en échec ne compte pas.</param>
/// <param name="LastSentOn">Jour de cette dernière relance.</param>
public readonly record struct ReminderState(int DaysOverdue, ReminderLevel? LastLevel, DateOnly? LastSentOn);

/// <summary>
/// Le scénario de relance, sans effet de bord : quel niveau envoyer aujourd'hui pour une facture due. Les niveaux se
/// suivent un par un (jamais de mise en demeure comme première relance), chacun une fois son délai atteint, et au moins
/// <see cref="ReminderSettings.MinimumGapDays"/> jours après la relance précédente.
/// </summary>
public static class ReminderPlanner
{
    /// <summary>Le niveau dû aujourd'hui, ou <c>null</c> si rien n'est à envoyer.</summary>
    public static ReminderLevel? Due(ReminderState state, ReminderSettings settings, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ReminderLevel? next = Next(state.LastLevel);
        if (next is not ReminderLevel level || state.DaysOverdue < settings.DelayOf(level))
        {
            return null;
        }

        bool tooSoon = state.LastSentOn is DateOnly last && today.DayNumber - last.DayNumber < settings.MinimumGapDays;
        return tooSoon ? null : level;
    }

    /// <summary>
    /// Le niveau de l'escalade qui suit <paramref name="last"/> (le rappel amical s'il n'y a rien eu, ou seulement un
    /// rappel avant échéance), ou <c>null</c> après la mise en demeure.
    /// </summary>
    public static ReminderLevel? Next(ReminderLevel? last) => last switch
    {
        null or ReminderLevel.BeforeDue => ReminderLevel.Friendly,
        ReminderLevel.FormalNotice => null,
        ReminderLevel.MissedInstallment => ReminderLevel.Reminder,
        ReminderLevel l => l + 1,
    };

    /// <summary>
    /// Le jour du rappel avant échéance : <paramref name="leadDays"/> jours avant l'échéance, avancé au meilleur jour du
    /// client (celui où ses paiements partent le plus vite) s'il en a un dans la semaine qui précède. Jamais le jour de
    /// l'émission ni avant : une facture à échéance courte n'a pas de rappel (<c>null</c>).
    /// </summary>
    public static DateOnly? BeforeDueOn(DateOnly issueDate, DateOnly dueDate, int leadDays, DayOfWeek? bestDay)
    {
        DateOnly target = dueDate.AddDays(-leadDays);
        if (bestDay is DayOfWeek day)
        {
            DateOnly best = target.AddDays(-((((int)target.DayOfWeek - (int)day) % 7 + 7) % 7));
            if (best > issueDate)
            {
                return best;
            }
        }

        return target > issueDate ? target : null;
    }
}

/// <summary>
/// Ce qu'une facture a déjà reçu : la dernière relance de l'escalade, le rappel avant échéance et la dernière relance
/// d'échéance d'échéancier. Seuls les envois partis ou notés comptent : un envoi en échec n'a rien fait.
/// </summary>
public sealed record ReminderTrack(PaymentReminder? LastEscalation, PaymentReminder? BeforeDue, PaymentReminder? LastInstallment)
{
    public static ReminderTrack Empty { get; } = new(null, null, null);

    public static ReminderTrack Of(IEnumerable<PaymentReminder> reminders)
    {
        ArgumentNullException.ThrowIfNull(reminders);
        List<PaymentReminder> sent = [.. reminders.Where(r => r.Outcome == ReminderOutcome.Sent)];
        return new(
            sent.Where(r => r.Level.IsEscalation()).MaxBy(r => (r.Level, r.OccurredAt)),
            sent.Where(r => r.Level == ReminderLevel.BeforeDue).MaxBy(r => r.OccurredAt),
            sent.Where(r => r.Level == ReminderLevel.MissedInstallment).MaxBy(r => (r.Installment, r.OccurredAt)));
    }

    /// <summary>Vrai si l'échéance <paramref name="number"/> (ou une suivante) a déjà eu sa relance.</summary>
    public bool InstallmentReminded(int number) => LastInstallment?.Installment >= number;

    /// <summary>La dernière relance faite, tous niveaux confondus.</summary>
    public PaymentReminder? Last => new[] { LastEscalation, BeforeDue, LastInstallment }.Where(r => r is not null).MaxBy(r => r!.OccurredAt);

    /// <summary>
    /// L'état de l'escalade. Après un échéancier non tenu ou arrêté, la relance d'échéance manquée a tenu lieu de rappel
    /// amical : l'escalade reprend à la relance, en respectant l'écart minimal depuis elle.
    /// </summary>
    public ReminderState Escalation(int daysOverdue)
    {
        ReminderLevel? level = LastEscalation?.Level;
        DateOnly? on = LastEscalation?.OccurredOn;
        if (LastInstallment is { } installment)
        {
            level = level is ReminderLevel known && known > ReminderLevel.Friendly ? known : ReminderLevel.Friendly;
            on = on is DateOnly escalated && escalated > installment.OccurredOn ? escalated : installment.OccurredOn;
        }

        return new ReminderState(daysOverdue, level, on);
    }
}
