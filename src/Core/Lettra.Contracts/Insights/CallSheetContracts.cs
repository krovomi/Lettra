using System.Text.Json.Serialization;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Reminders;

namespace Lettra.Contracts.Insights;

/// <summary>Un point à aborder pendant l'appel, du plus urgent au moins urgent.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CallPointKindDto>))]
public enum CallPointKindDto
{
    /// <summary>Une facture est contestée ou refusée à la plateforme : la régler avant de parler d'argent.</summary>
    Disputed = 1,

    /// <summary>Des factures ont plus de deux mois de retard : douteuses, sorties de la prévision.</summary>
    Doubtful = 2,

    /// <summary>La dernière relance est déjà partie : l'étape suivante est la mise en demeure.</summary>
    FinalSent = 3,

    /// <summary>Le client a des factures échues mais n'a encore reçu aucune relance.</summary>
    NeverReminded = 4,

    /// <summary>La dernière relance faite, sa date et son niveau.</summary>
    LastReminder = 5,

    /// <summary>L'indemnité forfaitaire de 40 € par facture en retard, due par un client professionnel.</summary>
    Indemnity = 6,

    /// <summary>L'habitude du client : retard habituel et part des factures payées à l'heure.</summary>
    Habit = 7,

    /// <summary>Le dernier encaissement reçu de ce client.</summary>
    LastPayment = 8,
}

/// <summary>Un point à aborder ; seuls les champs utiles à son genre sont remplis.</summary>
/// <param name="Kind">Le genre du point.</param>
/// <param name="Number">Le numéro de la facture concernée, s'il n'y en a qu'une.</param>
/// <param name="Count">Le nombre de factures concernées.</param>
/// <param name="Days">Des jours : retard habituel, ou plus grand retard.</param>
/// <param name="Percent">Un pourcentage : part des factures payées à l'heure.</param>
/// <param name="Amount">Un montant TTC en euros.</param>
/// <param name="Date">Une date : dernière relance, dernier encaissement.</param>
/// <param name="Level">Le niveau de la dernière relance.</param>
public sealed record CallPointResponse(
    CallPointKindDto Kind,
    string? Number = null,
    int? Count = null,
    int? Days = null,
    int? Percent = null,
    decimal? Amount = null,
    DateOnly? Date = null,
    ReminderLevelDto? Level = null);

/// <summary>Une facture encore due de ce client.</summary>
/// <param name="InvoiceId">La facture ou l'acompte.</param>
/// <param name="Number">Son numéro.</param>
/// <param name="IssueDate">Sa date d'émission.</param>
/// <param name="DueDate">Son échéance.</param>
/// <param name="OutstandingInclTax">Reste dû TTC.</param>
/// <param name="DaysLate">Jours de retard à ce jour (zéro si elle n'est pas échue).</param>
/// <param name="PlatformStatus">Le dernier statut à la plateforme agréée, si elle y a été transmise.</param>
/// <param name="LastReminderLevel">Le niveau de sa dernière relance faite.</param>
/// <param name="LastReminderOn">Le jour de sa dernière relance faite.</param>
public sealed record CallSheetInvoiceResponse(
    Guid InvoiceId,
    string Number,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    decimal OutstandingInclTax,
    int DaysLate,
    PlatformStatusDto? PlatformStatus,
    ReminderLevelDto? LastReminderLevel,
    DateOnly? LastReminderOn);

/// <summary>Une relance faite à ce client (les plus récentes d'abord).</summary>
public sealed record CallSheetReminderResponse(DateOnly On, string InvoiceNumber, ReminderLevelDto Level, ReminderChannelDto Channel);

/// <summary>
/// La fiche à lire avant d'appeler un client en retard : ce qu'il doit et depuis quand, ses habitudes, les relances
/// déjà faites et les points à aborder, dans l'ordre. Calculée à chaque lecture, sans rien écrire.
/// </summary>
/// <param name="Today">Date du jour (Paris).</param>
/// <param name="CustomerId">Le client.</param>
/// <param name="CustomerName">Son nom.</param>
/// <param name="Email">Son adresse de facturation, si elle est connue.</param>
/// <param name="Weather">Sa météo des paiements, s'il a un historique.</param>
/// <param name="OutstandingInclTax">Reste dû TTC de toutes ses factures.</param>
/// <param name="OverdueInclTax">Dont échu.</param>
/// <param name="Invoices">Ses factures dues, les plus en retard d'abord.</param>
/// <param name="Reminders">Ses dernières relances, au plus <see cref="MaxReminders"/>.</param>
/// <param name="Points">Les points à aborder, du plus urgent au moins urgent.</param>
public sealed record CallSheetResponse(
    DateOnly Today,
    Guid CustomerId,
    string CustomerName,
    string? Email,
    CustomerPaymentWeatherResponse? Weather,
    decimal OutstandingInclTax,
    decimal OverdueInclTax,
    IReadOnlyList<CallSheetInvoiceResponse> Invoices,
    IReadOnlyList<CallSheetReminderResponse> Reminders,
    IReadOnlyList<CallPointResponse> Points)
{
    public const int MaxReminders = 10;
}
