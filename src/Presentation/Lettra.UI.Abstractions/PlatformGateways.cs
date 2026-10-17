using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Transmission d'une pièce validée à la plateforme agréée (étape 5.3) : dépôt, statuts du cycle de vie (XP Z12-012)
/// et encaissements. Sans plateforme configurée, les écritures échouent avec <c>Platform.NotConfigured</c>.
/// </summary>
public interface IPlatformTransmissionGateway
{
    /// <summary>La dernière transmission de la pièce ; <c>Platform.NotTransmitted</c> si elle n'a jamais été transmise.</summary>
    Task<OperationResult<PlatformTransmissionResponse>> GetAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>Dépose la pièce (facture électronique ou e-reporting selon le client) ; une pièce déposée passe émise.</summary>
    Task<OperationResult<PlatformTransmissionResponse>> TransmitAsync(Guid invoiceId, TransmitInvoiceRequest request, CancellationToken cancellationToken);

    /// <summary>Relit les statuts à la plateforme.</summary>
    Task<OperationResult<PlatformTransmissionResponse>> RefreshAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>Déclare un encaissement, éventuellement partiel ; la transmission devient définitive une fois le TTC encaissé.</summary>
    Task<OperationResult<PlatformTransmissionResponse>> ReportPaymentAsync(Guid invoiceId, ReportPaymentRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Point des créances du dossier pour le tableau de bord : factures en retard (reste dû après avoirs et encaissements
/// déclarés) et état des transmissions à la plateforme agréée.
/// </summary>
public interface IReceivablesGateway
{
    Task<OperationResult<ReceivablesSummaryResponse>> GetSummaryAsync(CancellationToken cancellationToken);
}
