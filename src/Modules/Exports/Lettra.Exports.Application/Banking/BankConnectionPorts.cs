using Lettra.Domain.Banking;
using Lettra.Domain.Common;

namespace Lettra.Exports.Application.Banking;

/// <summary>Une banque que l'agrégateur sait relier.</summary>
public sealed record BankInstitution(string Id, string Name);

/// <summary>Un consentement commencé : la référence de l'agrégateur, et la page de la banque où le donner.</summary>
public sealed record BankConsentStart(string ProviderReference, string RedirectUrl);

/// <summary>Un consentement donné : le compte lisible (IBAN, jamais gardé en entier) et son échéance.</summary>
public sealed record BankConsentGrant(string AccountIban, DateTimeOffset ExpiresAt);

/// <summary>
/// Agrégateur bancaire (DSP2, accès aux comptes) : la banque montre la demande de consentement sur sa propre page, puis
/// l'agrégateur lit les opérations du compte tant que le consentement court. L'adaptateur réel (Bridge, Powens,
/// GoCardless…) se branche ici sans toucher aux cas d'usage ; la démo utilise un agrégateur simulé.
/// </summary>
public interface IBankAggregator
{
    /// <summary>Le nom affiché (« Agrégateur simulé »…).</summary>
    string Name { get; }

    /// <summary>Faux sans agrégateur configuré : le rapprochement ne propose alors que l'import de fichiers.</summary>
    bool IsAvailable { get; }

    Task<IReadOnlyList<BankInstitution>> ListInstitutionsAsync(CancellationToken cancellationToken);

    /// <summary>Commence le consentement pour la connexion <paramref name="connectionId"/> (rendue au retour de la banque).</summary>
    Task<BankConsentStart> StartConsentAsync(BankInstitution institution, Guid connectionId, CancellationToken cancellationToken);

    /// <summary>Le retour de la banque : accord (le compte et l'échéance) ou refus.</summary>
    Task<Result<BankConsentGrant>> CompleteConsentAsync(string providerReference, string? callbackCode, CancellationToken cancellationToken);

    /// <summary>Les opérations comptabilisées du compte entre deux dates (incluses), avec une référence bancaire stable.</summary>
    Task<Result<ParsedBankStatement>> FetchAsync(string providerReference, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Retire le consentement chez l'agrégateur (la connexion est déconnectée dans Lettra quoi qu'il réponde).</summary>
    Task RevokeAsync(string providerReference, CancellationToken cancellationToken);
}

/// <summary>Banques connectées du dossier.</summary>
public interface IBankConnectionRepository
{
    Task AddAsync(BankConnection connection, CancellationToken cancellationToken);

    Task<BankConnection?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Toutes les connexions du dossier, suivies (une synchronisation les modifie), les plus récentes d'abord.</summary>
    Task<IReadOnlyList<BankConnection>> ListAsync(CancellationToken cancellationToken);
}
