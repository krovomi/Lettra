using System.Globalization;
using Lettra.Domain.Common;

namespace Lettra.Domain.Review;

/// <summary>
/// Période d'une revue de dossier : un mois (<c>2026-09</c>) ou un trimestre (<c>2026-T3</c>). La forme écrite est aussi
/// la clé stockée : elle ne change jamais.
/// </summary>
public readonly record struct ReviewPeriod
{
    private ReviewPeriod(int year, int month, bool isQuarter)
    {
        Year = year;
        Month = month;
        IsQuarter = isQuarter;
    }

    public int Year { get; }

    /// <summary>Le mois (1 à 12), ou le numéro du trimestre (1 à 4) quand <see cref="IsQuarter"/>.</summary>
    public int Month { get; }

    public bool IsQuarter { get; }

    public DateOnly Start => IsQuarter ? new DateOnly(Year, ((Month - 1) * 3) + 1, 1) : new DateOnly(Year, Month, 1);

    /// <summary>Dernier jour de la période.</summary>
    public DateOnly End => Start.AddMonths(IsQuarter ? 3 : 1).AddDays(-1);

    public static ReviewPeriod OfMonth(int year, int month) =>
        month is < 1 or > 12 || year is < 2000 or > 9999 ? throw new ArgumentOutOfRangeException(nameof(month)) : new(year, month, false);

    /// <summary>Le mois écoulé : c'est celui que l'on revoit en début de mois.</summary>
    public static ReviewPeriod PreviousMonth(DateOnly today)
    {
        DateOnly previous = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        return OfMonth(previous.Year, previous.Month);
    }

    public static Result<ReviewPeriod> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length is < 6 or > 7 || text[4] != '-'
            || !int.TryParse(text.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out int year) || year < 2000)
        {
            return ReviewErrors.PeriodInvalid;
        }

        ReadOnlySpan<char> rest = text.AsSpan(5);
        if (rest.Length == 2 && rest[0] is 'T' or 't' && rest[1] is >= '1' and <= '4')
        {
            return new ReviewPeriod(year, rest[1] - '0', true);
        }

        return rest.Length == 2 && int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out int month) && month is >= 1 and <= 12
            ? new ReviewPeriod(year, month, false)
            : ReviewErrors.PeriodInvalid;
    }

    public override string ToString() => IsQuarter
        ? string.Create(CultureInfo.InvariantCulture, $"{Year:D4}-T{Month}")
        : string.Create(CultureInfo.InvariantCulture, $"{Year:D4}-{Month:D2}");
}
