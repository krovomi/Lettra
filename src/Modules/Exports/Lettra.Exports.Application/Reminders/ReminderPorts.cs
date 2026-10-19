using Lettra.Contracts.Reminders;
using Lettra.Domain.Billing;
using Lettra.Domain.Reminders;
using Lettra.Domain.SharedKernel;

namespace Lettra.Exports.Application.Reminders;

/// <summary>Ce qu'une relance doit savoir du client : où l'écrire, dans quelle langue, et s'il est professionnel (40 €).</summary>
public sealed record ReminderCustomer(CustomerId Id, string Name, string? Email, string? Language, bool IsProfessional);

/// <summary>Qui signe les relances : la fiche de l'entreprise du dossier.</summary>
public sealed record ReminderSender(string Name, string? Iban, string? Bic);

/// <summary>Filtre et tri de l'historique des relances.</summary>
public sealed record ReminderHistoryFilter(
    InvoiceId? Invoice,
    CustomerId? Customer,
    ReminderLevel? Level,
    ReminderOutcome? Outcome,
    ReminderChannel? Channel,
    DateOnly? From,
    DateOnly? To,
    string? Search,
    ReminderSortDto Sort,
    bool Descending,
    int Page,
    int PageSize);

/// <summary>Une relance de l'historique avec le nom du client (lecture seule).</summary>
public sealed record ReminderHistoryItem(PaymentReminder Reminder, string CustomerName);

/// <summary>Réglage du scénario de relance (une ligne par dossier, créée au premier réglage).</summary>
public interface IReminderSettingsRepository
{
    Task<ReminderSettings?> FindAsync(CancellationToken cancellationToken);

    Task AddAsync(ReminderSettings settings, CancellationToken cancellationToken);
}

/// <summary>Historique des relances : on ajoute, on ne modifie ni ne supprime jamais.</summary>
public interface IPaymentReminderRepository
{
    Task AddAsync(PaymentReminder reminder, CancellationToken cancellationToken);

    /// <summary>La dernière relance faite (partie ou notée, pas en échec) de chaque facture donnée.</summary>
    Task<IReadOnlyDictionary<InvoiceId, PaymentReminder>> LastSentAsync(IReadOnlyCollection<InvoiceId> invoices, CancellationToken cancellationToken);

    Task<(IReadOnlyList<ReminderHistoryItem> Items, int Total)> ListAsync(ReminderHistoryFilter filter, CancellationToken cancellationToken);
}

/// <summary>Lecture des clients, des taux de pénalité figés sur les factures et de la fiche de l'entreprise.</summary>
public interface IReminderContextReader
{
    Task<IReadOnlyDictionary<CustomerId, ReminderCustomer>> CustomersAsync(IReadOnlyCollection<CustomerId> customers, CancellationToken cancellationToken);

    /// <summary>Taux annuel des pénalités de retard figé à la validation de chaque facture (absent s'il n'y en a pas).</summary>
    Task<IReadOnlyDictionary<InvoiceId, decimal>> PenaltyRatesAsync(IReadOnlyCollection<InvoiceId> invoices, CancellationToken cancellationToken);

    Task<ReminderSender?> SenderAsync(CancellationToken cancellationToken);
}
