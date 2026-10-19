namespace Lettra.Domain.SharedKernel;

/// <summary>
/// Le calendrier légal français : dates des pièces, des encaissements, des échéances fiscales et des clôtures se lisent à
/// l'heure de Paris, jamais à celle du serveur (UTC). Un seul endroit résout le fuseau et choisit le repli.
/// </summary>
public static class LegalCalendar
{
    /// <summary>Identifiant IANA du fuseau légal.</summary>
    public const string TimeZoneId = "Europe/Paris";

    /// <summary>
    /// Le fuseau de Paris. Sur un runtime sans données de fuseaux (mode <c>InvariantTimezone</c>), repli sur UTC plutôt
    /// qu'une exception au chargement du type, qui rendrait inutilisable tout écran qui affiche une date.
    /// </summary>
    public static TimeZoneInfo Paris { get; } = Find();

    /// <summary>Le jour à Paris à l'instant donné par l'horloge.</summary>
    public static DateOnly Today(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return DateOf(clock.GetUtcNow());
    }

    /// <summary>Le jour à Paris de l'instant donné.</summary>
    public static DateOnly DateOf(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Paris).DateTime);

    private static TimeZoneInfo Find()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
