using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Banques connectées (rapprochement bancaire) : relier un compte par un agrégateur, recevoir le retour de la banque,
/// synchroniser, déconnecter. Lecture : <c>DossierLire</c> ; le reste : <c>FactureEmettre</c>. Sans agrégateur
/// configuré, la lecture rend <c>Available = false</c> et l'écran garde l'import de fichiers.
/// </summary>
public interface IBankConnectionsGateway
{
    Task<OperationResult<BankConnectionsResponse>> GetAsync(CancellationToken cancellationToken);

    /// <summary>Commence le consentement : l'adresse rendue est la page de la banque (relative à l'application pour la banque simulée).</summary>
    Task<OperationResult<BankConsentRedirectResponse>> StartAsync(StartBankConnectionRequest request, CancellationToken cancellationToken);

    Task<OperationResult<BankConnectionCompletedResponse>> CompleteAsync(Guid connectionId, CompleteBankConnectionRequest request, CancellationToken cancellationToken);

    Task<OperationResult<SyncBankConnectionsResponse>> SyncAsync(SyncBankConnectionsRequest request, CancellationToken cancellationToken);

    Task<OperationResult<BankConnectionResponse>> RevokeAsync(Guid connectionId, CancellationToken cancellationToken);
}
