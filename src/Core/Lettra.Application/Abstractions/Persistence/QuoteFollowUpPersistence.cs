using Lettra.Contracts.Billing;
using Lettra.Domain.Billing;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Persistence;

/// <summary>Où écrire au client d'un devis et dans quelle langue (<c>fr-FR</c> ou <c>en-US</c>, absente : français).</summary>
public sealed record QuoteCustomerContact(string? Email, string? Language);

/// <summary>Lecture des devis numérotés pour les devis à relancer et le carnet de commandes (lecture seule).</summary>
public interface IQuotePipelineReader
{
    /// <summary>
    /// Les devis numérotés émis depuis <paramref name="from"/>, plus ceux encore en attente de réponse quelle que soit leur
    /// date. Le jour de réponse d'un devis accepté ou refusé avant qu'il soit gardé est celui de sa dernière modification.
    /// </summary>
    Task<IReadOnlyList<QuoteFact>> ListAsync(DateOnly from, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<CustomerId, QuoteCustomerContact>> ContactsAsync(IReadOnlyCollection<CustomerId> customers, CancellationToken cancellationToken);

    /// <summary>
    /// Les clients opposés au profilage (RGPD, art. 21), notés depuis les relances : le carnet de commandes les tient à
    /// l'écart de ses taux et ne pondère pas leurs devis.
    /// </summary>
    Task<IReadOnlySet<CustomerId>> ObjectingCustomersAsync(CancellationToken cancellationToken);
}

/// <summary>Les relances faites (parties ou notées, pas en échec) d'un devis : combien, et la dernière.</summary>
public sealed record QuoteFollowUpStats(int Count, QuoteFollowUp Last);

/// <summary>Filtre et tri de l'historique des relances de devis.</summary>
public sealed record QuoteFollowUpHistoryFilter(
    QuoteId? Quote,
    CustomerId? Customer,
    QuoteFollowUpChannel? Channel,
    QuoteFollowUpOutcome? Outcome,
    DateOnly? From,
    DateOnly? To,
    string? Search,
    QuoteFollowUpSortDto Sort,
    bool Descending,
    int Page,
    int PageSize);

/// <summary>Une relance de l'historique avec le nom du client du devis.</summary>
public sealed record QuoteFollowUpHistoryItem(QuoteFollowUp FollowUp, string CustomerName);

/// <summary>Historique des relances de devis : on ajoute, on ne modifie ni ne supprime jamais.</summary>
public interface IQuoteFollowUpRepository
{
    Task AddAsync(QuoteFollowUp followUp, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<QuoteId, QuoteFollowUpStats>> StatsAsync(IReadOnlyCollection<QuoteId> quotes, CancellationToken cancellationToken);

    Task<(IReadOnlyList<QuoteFollowUpHistoryItem> Items, int Total)> ListAsync(QuoteFollowUpHistoryFilter filter, CancellationToken cancellationToken);
}

/// <summary>Réglage des devis à relancer (une ligne par dossier, créée au premier réglage).</summary>
public interface IQuoteFollowUpSettingsRepository
{
    Task<QuoteFollowUpSettings?> FindAsync(CancellationToken cancellationToken);

    Task AddAsync(QuoteFollowUpSettings settings, CancellationToken cancellationToken);
}
