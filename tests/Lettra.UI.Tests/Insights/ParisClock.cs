namespace Lettra.UI.Tests.Insights;

/// <summary>Une horloge arrêtée, à l'heure de Paris, pour les écrans qui lisent le jour et l'heure du poste.</summary>
internal sealed class ParisClock(DateTimeOffset now) : TimeProvider
{
    public static TimeZoneInfo Paris { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => Paris;
}
