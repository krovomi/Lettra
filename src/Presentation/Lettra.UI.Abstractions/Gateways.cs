using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Le seul contrat que l'écran Clients connaît. Deux implémentations : in-process (MAUI, modes 1 et 4)
/// et HTTP (WASM, MAUI modes 2, 3 et 5). Même résultat, même codes d'erreur.
/// </summary>
public interface ICustomersGateway
{
    Task<OperationResult<CustomerResponse>> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken);

    Task<OperationResult<PagedResponse<CustomerSummary>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<OperationResult<CustomerResponse>> GetAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>Dossier sur lequel l'utilisateur travaille. Phase 0 : fixé par la configuration ; le sélecteur de dossier arrive en phase 2.</summary>
public interface IDossierSelection
{
    Guid? CurrentDossierId { get; }
}

/// <summary>Sélection de dossier fixe (configuration).</summary>
public sealed class FixedDossierSelection(Guid? dossierId) : IDossierSelection
{
    public Guid? CurrentDossierId { get; } = dossierId;
}

/// <summary>
/// Session de l'utilisateur. In-process (MAUI, mode 1) : compte local à phrase secrète. HTTP (navigateur) : session du
/// BFF, la connexion et la déconnexion se font par navigation pleine page vers <see cref="SignInPath"/> et
/// <see cref="Contracts.Identity.SessionResponse.SignOutUrl"/>.
/// </summary>
public interface ISessionGateway
{
    /// <summary>Adresse de connexion par navigation (BFF) ; <c>null</c> pour le compte local.</summary>
    string? SignInPath { get; }

    Task<OperationResult<Contracts.Identity.SessionResponse>> GetAsync(CancellationToken cancellationToken);

    Task<OperationResult<Contracts.Identity.SessionResponse>> CreateLocalAccountAsync(Contracts.Identity.CreateLocalAccountRequest request, CancellationToken cancellationToken);

    Task<OperationResult<Contracts.Identity.SessionResponse>> UnlockAsync(Contracts.Identity.UnlockLocalSessionRequest request, CancellationToken cancellationToken);

    Task<OperationResult<Contracts.Identity.SessionResponse>> LockAsync(CancellationToken cancellationToken);
}
