namespace Lettra.Domain.Billing;

/// <summary>
/// Délais de paiement entre professionnels (Code de commerce, art. L441-10) et tranches de retard du rapport de gestion
/// (art. D441-6, tableau de l'annexe 4-1). Le délai convenu ne peut dépasser 60 jours à compter de la date de facture, ou
/// par dérogation 45 jours fin de mois. Les délais propres à certains secteurs (transport, denrées périssables, export)
/// ne sont pas traités.
/// </summary>
public static class PaymentDelays
{
    /// <summary>Plafond général : 60 jours à compter de la date d'émission de la facture.</summary>
    public const int LegalDays = 60;

    /// <summary>Plafond dérogatoire : 45 jours fin de mois.</summary>
    public const int EndOfMonthDays = 45;

    /// <summary>Nombre de tranches du tableau : « 0 jour », 1 à 30, 31 à 60, 61 à 90, 91 jours et plus.</summary>
    public const int BracketCount = 5;

    /// <summary>
    /// Dernière échéance permise pour une facture émise le <paramref name="issueDate"/> : la plus tardive des trois
    /// lectures admises (60 jours ; fin du mois puis 45 jours ; 45 jours puis fin du mois). Au-delà, le délai convenu
    /// dépasse le plafond légal quelle que soit la lecture retenue par le contrat.
    /// </summary>
    public static DateOnly LegalCeiling(DateOnly issueDate)
    {
        DateOnly sixtyDays = issueDate.AddDays(LegalDays);
        DateOnly endOfMonthThen45 = EndOfMonth(issueDate).AddDays(EndOfMonthDays);
        DateOnly fortyFiveThenEndOfMonth = EndOfMonth(issueDate.AddDays(EndOfMonthDays));
        DateOnly latest = sixtyDays > endOfMonthThen45 ? sixtyDays : endOfMonthThen45;
        return latest > fortyFiveThenEndOfMonth ? latest : fortyFiveThenEndOfMonth;
    }

    /// <summary>Jours de dépassement du plafond légal (0 si l'échéance le respecte).</summary>
    public static int DaysOverCeiling(DateOnly issueDate, DateOnly dueDate) =>
        Math.Max(0, dueDate.DayNumber - LegalCeiling(issueDate).DayNumber);

    /// <summary>Jours de retard à une date : 0 le jour de l'échéance, négatif avant.</summary>
    public static int DaysLate(DateOnly dueDate, DateOnly asOf) => asOf.DayNumber - dueDate.DayNumber;

    /// <summary>
    /// Tranche du tableau pour une facture échue : 0 pour « 0 jour (indicatif) », échue le jour même ; 1 pour 1 à
    /// 30 jours ; 2 pour 31 à 60 ; 3 pour 61 à 90 ; 4 pour 91 jours et plus.
    /// </summary>
    public static int Bracket(int daysLate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(daysLate);
        return daysLate switch
        {
            0 => 0,
            <= 30 => 1,
            <= 60 => 2,
            <= 90 => 3,
            _ => 4,
        };
    }

    private static DateOnly EndOfMonth(DateOnly date) => new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
}
