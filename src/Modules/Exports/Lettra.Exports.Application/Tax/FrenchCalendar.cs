namespace Lettra.Exports.Application.Tax;

/// <summary>
/// Jours fériés de la métropole (lundi de Pâques, Ascension et lundi de Pentecôte compris), jours ouvrés (lundi à
/// vendredi hors fériés) et jours ouvrables (lundi à samedi hors fériés). Les jours propres à l'Alsace-Moselle et aux
/// DOM ne sont pas comptés.
/// </summary>
public static class FrenchCalendar
{
    public static bool IsHoliday(DateOnly date)
    {
        if ((date.Month, date.Day) is (1, 1) or (5, 1) or (5, 8) or (7, 14) or (8, 15) or (11, 1) or (11, 11) or (12, 25))
        {
            return true;
        }

        DateOnly easter = EasterSunday(date.Year);
        return date == easter.AddDays(1) || date == easter.AddDays(39) || date == easter.AddDays(50);
    }

    /// <summary>Lundi à vendredi, hors jours fériés.</summary>
    public static bool IsWorkingDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !IsHoliday(date);

    /// <summary>Lundi à samedi, hors jours fériés.</summary>
    public static bool IsOuvrable(DateOnly date) => date.DayOfWeek != DayOfWeek.Sunday && !IsHoliday(date);

    /// <summary>Le <paramref name="rank"/>-ième jour ouvré strictement après <paramref name="date"/>.</summary>
    public static DateOnly WorkingDayAfter(DateOnly date, int rank) => Nth(date, rank, IsWorkingDay);

    /// <summary>Le <paramref name="rank"/>-ième jour ouvrable du mois de <paramref name="month"/>.</summary>
    public static DateOnly OuvrableDayOfMonth(DateOnly month, int rank) => Nth(new DateOnly(month.Year, month.Month, 1).AddDays(-1), rank, IsOuvrable);

    /// <summary>Dimanche de Pâques (algorithme de Meeus pour le calendrier grégorien).</summary>
    public static DateOnly EasterSunday(int year)
    {
        int a = year % 19;
        int b = year / 100;
        int c = year % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = ((19 * a) + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        int m = (a + (11 * h) + (22 * l)) / 451;
        int month = (h + l - (7 * m) + 114) / 31;
        int day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }

    private static DateOnly Nth(DateOnly after, int rank, Func<DateOnly, bool> counts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rank, 1);
        DateOnly day = after;
        int found = 0;
        while (found < rank)
        {
            day = day.AddDays(1);
            if (counts(day))
            {
                found++;
            }
        }

        return day;
    }
}
