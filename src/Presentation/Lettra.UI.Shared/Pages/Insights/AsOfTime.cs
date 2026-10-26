using System.Globalization;

namespace Lettra.UI.Shared.Pages.Insights;

/// <summary>L'instant choisi sur l'écran « Remonter le temps » : une heure saisie, puis le jour et l'heure du poste.</summary>
public static class AsOfTime
{
    private static readonly string[] Formats = ["%H", "HH", "H:mm", "HH:mm", "H'h'", "HH'h'", "H'h'mm", "HH'h'mm"];

    /// <summary>Lit « 18:00 », « 18h00 », « 18h » ou « 18 ». Vide : minuit moins une minute, la fin de la journée.</summary>
    public static bool TryParse(string? text, out TimeOnly time)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            time = new TimeOnly(23, 59);
            return true;
        }

        return TimeOnly.TryParseExact(text.Trim().ToUpperInvariant().Replace('H', 'h'), Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    /// <summary>
    /// Le jour et l'heure dans le fuseau du poste. Une heure sautée au passage à l'heure d'été (2 h 30 un dernier dimanche
    /// de mars) devient l'heure qui suit ; une heure vécue deux fois à l'automne prend la première.
    /// </summary>
    public static DateTimeOffset Instant(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        DateTime local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        TimeSpan offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}
