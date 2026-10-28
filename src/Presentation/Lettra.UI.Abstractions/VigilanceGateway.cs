using Lettra.Contracts.Common;
using Lettra.Contracts.Vigilance;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Fiche anti-blanchiment du client. Lire demande <c>VigilanceConsulter</c> ; la remplir ou la revoir,
/// <c>VigilanceGerer</c>. Refus possibles : <c>Vigilance.*</c>, <c>Concurrency.VersionMismatch</c>.
/// </summary>
public interface IVigilanceGateway
{
    Task<OperationResult<VigilanceOverviewResponse>> GetAsync(CancellationToken cancellationToken);

    /// <summary>Crée la fiche (<paramref name="expectedVersion"/> vide) ou la revoit à partir de la version lue.</summary>
    Task<OperationResult<VigilanceOverviewResponse>> SaveAsync(SaveDueDiligenceRequest request, long? expectedVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Les bénéficiaires effectifs de l'entreprise du dossier lus au registre, criblés sur le registre des gels (demande
    /// <c>VigilanceGerer</c>). Rien n'est enregistré : l'utilisateur relit puis enregistre la fiche. Refus possible :
    /// <c>Directory.Unavailable</c>.
    /// </summary>
    Task<OperationResult<RegisteredOwnersResponse>> LookupOwnersAsync(CancellationToken cancellationToken);
}
