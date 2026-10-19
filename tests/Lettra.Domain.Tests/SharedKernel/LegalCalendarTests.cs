using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Tests.SharedKernel;

/// <summary>Le jour légal est celui de Paris, pas celui du serveur (UTC).</summary>
public sealed class LegalCalendarTests
{
    [Fact]
    public void Le_fuseau_est_celui_de_Paris() =>
        LegalCalendar.Paris.HasSameRules(TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris")).Should().BeTrue();

    [Theory]
    [InlineData("2026-12-31T22:59:59Z", "2026-12-31")] // 23 h 59 à Paris (UTC+1 en hiver)
    [InlineData("2026-12-31T23:00:00Z", "2027-01-01")] // minuit à Paris : déjà l'année suivante
    [InlineData("2026-06-30T21:59:59Z", "2026-06-30")] // 23 h 59 à Paris (UTC+2 en été)
    [InlineData("2026-06-30T22:00:00Z", "2026-07-01")]
    public void Le_jour_change_a_minuit_heure_de_Paris(string utc, string expected)
    {
        FixedClock clock = new(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture));

        LegalCalendar.Today(clock).Should().Be(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Le_jour_d_un_instant_ne_depend_pas_de_son_decalage()
    {
        DateTimeOffset utc = new(2026, 3, 29, 22, 30, 0, TimeSpan.Zero);

        LegalCalendar.DateOf(utc.ToOffset(TimeSpan.FromHours(-5))).Should().Be(new DateOnly(2026, 3, 30));
    }

    [Fact]
    public void Sans_horloge_le_calendrier_refuse() =>
        FluentActions.Invoking(() => LegalCalendar.Today(null!)).Should().Throw<ArgumentNullException>();

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
