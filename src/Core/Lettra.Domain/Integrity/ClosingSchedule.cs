namespace Lettra.Domain.Integrity;

/// <summary>Une période à clôturer : ses dates (heure de Paris) et l'instant UTC où elle se termine.</summary>
public sealed record ClosingPeriod(ClosingPeriodKind Kind, DateOnly Start, DateOnly End, DateTimeOffset EndsAt);

/// <summary>
/// Clôtures mensuelles, à l'heure de Paris : chaque mois écoulé depuis la première entrée du journal est clôturé,
/// même vide, pour que la chaîne des clôtures n'ait pas de trou. L'exercice comptable (clôture annuelle) dépend des
/// paramètres du dossier et viendra avec eux.
/// </summary>
public sealed class ClosingSchedule(TimeZoneInfo zone)
{
    public static ClosingSchedule Paris { get; } = new(TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"));

    /// <summary>Les mois entièrement écoulés à <paramref name="now"/> et pas encore clôturés.</summary>
    /// <param name="lastClosedEnd">Fin de la dernière clôture, ou <c>null</c> s'il n'y en a pas.</param>
    /// <param name="firstEntryAt">Première entrée du journal, ou <c>null</c> s'il est vide (rien à clôturer).</param>
    /// <param name="now">Instant présent.</param>
    public IReadOnlyList<ClosingPeriod> DueMonths(DateOnly? lastClosedEnd, DateTimeOffset? firstEntryAt, DateTimeOffset now)
    {
        DateOnly start;
        if (lastClosedEnd is DateOnly end)
        {
            start = end.AddDays(1);
        }
        else if (firstEntryAt is DateTimeOffset first)
        {
            DateOnly local = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(first, zone).DateTime);
            start = new DateOnly(local.Year, local.Month, 1);
        }
        else
        {
            return [];
        }

        List<ClosingPeriod> due = [];
        while (true)
        {
            DateOnly next = start.AddMonths(1);
            DateTimeOffset endsAt = StartOfDay(next);
            if (endsAt > now)
            {
                return due;
            }

            due.Add(new ClosingPeriod(ClosingPeriodKind.Month, start, next.AddDays(-1), endsAt));
            start = next;
        }
    }

    private DateTimeOffset StartOfDay(DateOnly day)
    {
        DateTime local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
