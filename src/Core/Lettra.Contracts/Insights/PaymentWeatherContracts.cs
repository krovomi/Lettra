namespace Lettra.Contracts.Insights;

/// <summary>
/// La météo d'un client, tirée de ses habitudes de paiement et recalculée à chaque lecture. Ensoleillé : il paie
/// à l'échéance ; nuageux : quelques jours de retard ; pluvieux : plusieurs semaines ; orageux : plus d'un mois, ou une facture qui traîne depuis plus de deux mois.
/// </summary>
public enum PaymentWeatherDto
{
    /// <summary>Pas encore assez d'historique (moins de deux factures réglées ou échues).</summary>
    Unknown = 0,
    Sunny = 1,
    Cloudy = 2,
    Rainy = 3,
    Stormy = 4,
}

/// <summary>Une facture encore due et la date à laquelle Lettra s'attend à la voir encaissée.</summary>
/// <param name="InvoiceId">La facture ou l'acompte.</param>
/// <param name="Number">Son numéro.</param>
/// <param name="DueDate">Son échéance, si elle en a une.</param>
/// <param name="OutstandingInclTax">Reste dû TTC.</param>
/// <param name="ExpectedDate">Échéance décalée du retard habituel du client, jamais avant demain.</param>
/// <param name="Doubtful">Plus de <see cref="PaymentWeatherResponse.StormOverdueDays"/> jours de retard : hors prévision.</param>
public sealed record ExpectedCollectionResponse(
    Guid InvoiceId,
    string Number,
    DateOnly? DueDate,
    decimal OutstandingInclTax,
    DateOnly ExpectedDate,
    bool Doubtful = false);

/// <summary>
/// La météo d'un client et ce qui la justifie, en chiffres simples.
/// </summary>
/// <param name="CustomerId">Le client.</param>
/// <param name="CustomerName">Son nom.</param>
/// <param name="Weather">Sa météo.</param>
/// <param name="Samples">Factures qui fondent la météo : réglées, ou échues et encore dues (douze derniers mois).</param>
/// <param name="AverageDaysLate">Retard moyen pondéré par les montants, en jours après l'échéance (négatif : en avance).</param>
/// <param name="TypicalDaysLate">Retard médian, en jours ; c'est lui qui décale les encaissements attendus.</param>
/// <param name="OnTimePercent">Part des factures réglées à l'échéance ou avant, en pourcentage.</param>
/// <param name="MaxDaysOverdue">Retard de la plus ancienne facture échue encore due, zéro s'il n'y en a pas.</param>
/// <param name="BestSendDay">Jour d'émission après lequel ce client a payé le plus vite (rien si l'historique ne le dit pas).</param>
/// <param name="BestSendDayGainDays">Jours gagnés en moyenne en émettant ce jour-là plutôt que les autres.</param>
/// <param name="OutstandingInclTax">Reste dû TTC de toutes ses factures.</param>
/// <param name="Expected">Ses factures dues, de la plus tôt attendue à la plus tard.</param>
public sealed record CustomerPaymentWeatherResponse(
    Guid CustomerId,
    string CustomerName,
    PaymentWeatherDto Weather,
    int Samples,
    int AverageDaysLate,
    int TypicalDaysLate,
    int OnTimePercent,
    int MaxDaysOverdue,
    DayOfWeek? BestSendDay,
    int BestSendDayGainDays,
    decimal OutstandingInclTax,
    IReadOnlyList<ExpectedCollectionResponse> Expected);

/// <summary>Les encaissements attendus d'une semaine (du lundi au dimanche).</summary>
public sealed record CollectionForecastWeekResponse(DateOnly WeekStart, decimal ExpectedInclTax, int Invoices);

/// <summary>
/// La météo des paiements du dossier : un bulletin par client (les plus orageux d'abord) et la prévision des
/// encaissements des huit prochaines semaines, que la trésorerie reprend.
/// </summary>
/// <param name="Today">Date du jour (Paris) du calcul.</param>
/// <param name="Customers">Les bulletins, les plus orageux d'abord.</param>
/// <param name="Forecast">Les huit semaines, à partir du lundi de la semaine de demain.</param>
/// <param name="Later">Encaissements attendus au-delà des huit semaines.</param>
/// <param name="Doubtful">Restes dus des factures douteuses, sortis de la prévision.</param>
public sealed record PaymentWeatherResponse(
    DateOnly Today,
    IReadOnlyList<CustomerPaymentWeatherResponse> Customers,
    IReadOnlyList<CollectionForecastWeekResponse> Forecast,
    decimal Later,
    decimal Doubtful = 0m)
{
    public const int ForecastWeeks = 8;

    /// <summary>Au-delà de ce retard, une seule facture due suffit à mettre le client sous l'orage.</summary>
    public const int StormOverdueDays = 60;
}
