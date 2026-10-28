using Lettra.Contracts.Common;
using Lettra.Contracts.Vigilance;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Vérification des clients et fournisseurs auprès des registres officiels. Lire demande <c>VigilanceConsulter</c> ;
/// vérifier, <c>VigilanceGerer</c>. Refus possibles : <c>ThirdPartyCheck.PartyNotFound</c>, <c>Directory.Unavailable</c>.
/// </summary>
public interface IThirdPartyChecksGateway
{
    Task<OperationResult<ThirdPartyChecksResponse>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Interroge les registres pour un tiers et garde la réponse ; rend sa ligne à jour.</summary>
    Task<OperationResult<ThirdPartyCheckRow>> CheckAsync(ThirdPartyKindDto kind, Guid partyId, CancellationToken cancellationToken);
}
