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

    /// <summary>Ce que chaque facture donnée a déjà reçu (relances parties ou notées, pas en échec).</summary>
    Task<IReadOnlyDictionary<InvoiceId, ReminderTrack>> TracksAsync(IReadOnlyCollection<InvoiceId> invoices, CancellationToken cancellationToken);

    Task<(IReadOnlyList<ReminderHistoryItem> Items, int Total)> ListAsync(ReminderHistoryFilter filter, CancellationToken cancellationToken);
}

/// <summary>Lecture des clients, des taux de pénalité figés sur les factures et de la fiche de l'entreprise.</summary>
public interface IReminderContextReader
{
    Task<IReadOnlyDictionary<CustomerId, ReminderCustomer>> CustomersAsync(IReadOnlyCollection<CustomerId> customers, CancellationToken cancellationToken);

    /// <summary>Taux annuel des pénalités de retard figé à la validation de chaque facture (absent s'il n'y en a pas).</summary>
    Task<IReadOnlyDictionary<InvoiceId, decimal>> PenaltyRatesAsync(IReadOnlyCollection<InvoiceId> invoices, CancellationToken cancellationToken);

    Task<ReminderSender?> SenderAsync(CancellationToken cancellationToken);

    /// <summary>Les factures données que l'acheteur conteste (litige déclaré sur la plateforme agréée).</summary>
    Task<IReadOnlySet<InvoiceId>> DisputedAsync(IReadOnlyCollection<InvoiceId> invoices, CancellationToken cancellationToken);
}

/// <summary>Les échéanciers du dossier.</summary>
public interface IPaymentPlanRepository
{
    Task AddAsync(PaymentPlan plan, CancellationToken cancellationToken);

    Task<PaymentPlan?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Les échéanciers en cours (pas arrêtés), au plus un par facture ; de tout le dossier sans <paramref name="invoices"/>.</summary>
    Task<IReadOnlyDictionary<InvoiceId, PaymentPlan>> ActiveAsync(IReadOnlyCollection<InvoiceId>? invoices, CancellationToken cancellationToken);

    /// <summary>Le dernier échéancier demandé pour une facture, arrêté ou non.</summary>
    Task<PaymentPlan?> LatestForInvoiceAsync(InvoiceId invoice, CancellationToken cancellationToken);

    /// <summary>Les échéanciers demandés depuis <paramref name="since"/>, et tous ceux encore en cours.</summary>
    Task<IReadOnlyList<PaymentPlan>> ListAsync(DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Les échéanciers que l'entreprise n'a pas encore vus.</summary>
    Task<int> CountUnacknowledgedAsync(CancellationToken cancellationToken);
}

/// <summary>Les oppositions des clients au ciblage selon leur comportement de paiement.</summary>
public interface IPaymentTargetingObjectionRepository
{
    Task<IReadOnlyList<PaymentTargetingObjection>> ListAsync(CancellationToken cancellationToken);

    Task<PaymentTargetingObjection?> FindAsync(CustomerId customer, CancellationToken cancellationToken);

    Task AddAsync(PaymentTargetingObjection objection, CancellationToken cancellationToken);

    /// <summary>Retire l'opposition (effacée à l'enregistrement suivant).</summary>
    Task RemoveAsync(PaymentTargetingObjection objection, CancellationToken cancellationToken);
}

/// <summary>
/// Le QR code de virement (norme EPC) joint au rappel avant échéance et à la relance d'échéance manquée, en image PNG.
/// Par défaut, aucun : le texte donne l'IBAN et la référence, et l'espace client montre le QR code.
/// </summary>
public interface IPaymentQrCodeImage
{
    /// <summary>L'image, ou <c>null</c> quand le virement ne peut pas être prérempli (IBAN absent ou invalide…).</summary>
    byte[]? Png(string beneficiary, string? iban, string? bic, decimal amount, string remittance);
}

/// <summary>Sans générateur de QR code : rien n'est joint.</summary>
internal sealed class NoPaymentQrCodeImage : IPaymentQrCodeImage
{
    public byte[]? Png(string beneficiary, string? iban, string? bic, decimal amount, string remittance) => null;
}
