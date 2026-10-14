namespace Lettra.Supervision.Domain;

/// <summary>Une étape d'escalade : au-delà de ce délai sans prise en charge, on prévient le niveau suivant.</summary>
/// <param name="Level">1 = second niveau d'astreinte (S1) ou manager (S2) ; 2 = responsable technique (S1).</param>
/// <param name="Due">Instant à partir duquel l'étape s'applique.</param>
public sealed record EscalationStep(int Level, DateTimeOffset Due);

/// <summary>
/// Délais de prise en charge et escalade automatique (annexe G §5), entièrement déterministes :
/// S1 → 15 min (24 h sur 24), puis niveau 1 à 15 min et niveau 2 à 30 min ; S2 → 4 heures ouvrées, puis le manager ;
/// S3 → 2 jours ouvrés ; S4 → aucun délai. Vigie n'intervient jamais ici.
/// </summary>
public sealed class EscalationPolicy(BusinessCalendar calendar)
{
    public const string RuleId = "escalade-automatique";

    public static readonly TimeSpan S1Takeover = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan S1SecondEscalation = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan S2Takeover = TimeSpan.FromHours(4);

    /// <summary>Échéance de prise en charge, ou <c>null</c> pour une S4.</summary>
    public DateTimeOffset? TakeoverDue(Alert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);
        return alert.Severity switch
        {
            Severity.S1 => alert.OpenedAt + S1Takeover,
            Severity.S2 => calendar.AddBusinessTime(alert.OpenedAt, S2Takeover),
            Severity.S3 => calendar.AddBusinessTime(alert.OpenedAt, calendar.BusinessDay * 2),
            _ => null,
        };
    }

    /// <summary>Les étapes d'escalade de la gravité courante.</summary>
    public IReadOnlyList<EscalationStep> StepsFor(Alert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);
        return alert.Severity switch
        {
            Severity.S1 => [new(1, alert.OpenedAt + S1Takeover), new(2, alert.OpenedAt + S1SecondEscalation)],
            Severity.S2 => [new(1, calendar.AddBusinessTime(alert.OpenedAt, S2Takeover))],
            _ => [],
        };
    }

    /// <summary>
    /// Applique l'escalade due à <paramref name="now"/> sur une alerte que personne n'a prise en charge.
    /// Renvoie le niveau atteint s'il vient de changer, sinon <c>null</c>.
    /// </summary>
    public int? Apply(Alert alert, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(alert);
        if (alert.State != AlertState.New)
        {
            return null;
        }

        EscalationStep? due = StepsFor(alert).Where(s => s.Due <= now).MaxBy(s => s.Level);
        if (due is null || due.Level <= alert.EscalationLevel)
        {
            return null;
        }

        alert.Escalate(due.Level, RuleId, now);
        return due.Level;
    }
}
