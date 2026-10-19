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

    /// <summary>Le niveau qui suit <paramref name="last"/> (le rappel amical s'il n'y a rien eu), ou <c>null</c> après la mise en demeure.</summary>
    public static ReminderLevel? Next(ReminderLevel? last) => last switch
    {
        null => ReminderLevel.Friendly,
        ReminderLevel.FormalNotice => null,
        ReminderLevel l => l + 1,
    };
}
