using Lettra.Contracts.Common;
using Lettra.Contracts.Vigilance;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Attestations de vigilance des fournisseurs. Lire demande <c>TiersConsulter</c> ; enregistrer, <c>TiersModifier</c>.
/// Refus possibles : <c>Attestation.*</c>, <c>Concurrency.VersionMismatch</c>.
/// </summary>
public interface IAttestationsGateway
{
    Task<OperationResult<SupplierAttestationsResponse>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Suit le fournisseur (<paramref name="expectedVersion"/> vide) ou met à jour son suivi à partir de la version lue.</summary>
    Task<OperationResult<SupplierAttestationsResponse>> SaveAsync(Guid supplierId, SaveAttestationRequest request, long? expectedVersion, CancellationToken cancellationToken);
}
