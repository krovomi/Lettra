namespace Lettra.UI.Shared.Services;

/// <summary>
/// La date du jour de l'écran, lue sur l'horloge injectée (<see cref="TimeProvider"/>) et dans le fuseau du poste :
/// les tests et la démonstration la fixent, les écrans ne lisent jamais l'horloge système directement.
/// </summary>
public static class UiClock
{
    public static DateOnly LocalToday(this TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
    }
}
