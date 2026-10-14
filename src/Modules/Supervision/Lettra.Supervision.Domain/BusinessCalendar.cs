namespace Lettra.Supervision.Domain;

/// <summary>
/// Heures ouvrées pour les délais S2 et S3 (annexe G §5) : du lundi au vendredi, 9 h à 18 h, à l'heure de Paris.
/// Les jours fériés et les horaires propres à un cabinet viendront avec la configuration des canaux (phase 3).
/// </summary>
public sealed class BusinessCalendar
{
    private readonly TimeZoneInfo _zone;

    public BusinessCalendar(TimeZoneInfo zone, TimeOnly opens, TimeOnly closes)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (closes <= opens)
        {
            throw new ArgumentException("La fermeture doit suivre l'ouverture.", nameof(closes));
        }

        _zone = zone;
        Opens = opens;
        Closes = closes;
    }

    public static BusinessCalendar Paris { get; } =
        new(TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"), new TimeOnly(9, 0), new TimeOnly(18, 0));

    public TimeOnly Opens { get; }

    public TimeOnly Closes { get; }

    /// <summary>Instant où <paramref name="duration"/> d'heures ouvrées se sont écoulées depuis <paramref name="start"/>.</summary>
    public DateTimeOffset AddBusinessTime(DateTimeOffset start, TimeSpan duration)
    {
        DateTime local = TimeZoneInfo.ConvertTime(start, _zone).DateTime;
        TimeSpan remaining = duration;
        while (true)
        {
            local = NextOpenMoment(local);
            DateTime closing = local.Date + Closes.ToTimeSpan();
            TimeSpan available = closing - local;
            if (remaining <= available)
            {
                DateTime due = local + remaining;
                return new DateTimeOffset(due, _zone.GetUtcOffset(due));
            }

            remaining -= available;
            local = closing;
        }
    }

    /// <summary>Durée d'un jour ouvré, pour exprimer « 2 jours ouvrés ».</summary>
    public TimeSpan BusinessDay => Closes - Opens;

    private DateTime NextOpenMoment(DateTime local)
    {
        while (true)
        {
            bool weekday = local.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
            TimeOnly time = TimeOnly.FromDateTime(local);
            if (weekday && time >= Opens && time < Closes)
            {
                return local;
            }

            if (weekday && time < Opens)
            {
                return local.Date + Opens.ToTimeSpan();
            }

            local = local.Date.AddDays(1) + Opens.ToTimeSpan();
            if (local.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                return local;
            }
        }
    }
}
