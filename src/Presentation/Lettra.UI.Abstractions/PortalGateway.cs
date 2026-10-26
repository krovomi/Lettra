using Lettra.Contracts.Common;
using Lettra.Contracts.Portal;

namespace Lettra.UI.Abstractions;

/// <summary>Un lien d'espace client créé, avec l'adresse complète à envoyer au client.</summary>
public sealed record PortalLinkCreated(PortalLinkResponse Link, string Url);

/// <summary>
/// Espace client, côté cabinet : liens envoyés au client (espace complet, ou signature d'un devis avec acompte) et preuve
/// de signature d'un devis. Création et révocation : <c>FactureEmettre</c> ; liste : <c>TiersConsulter</c> ; preuve :
/// <c>DossierLire</c>. Sur un poste isolé, l'espace client est fermé (<c>Portal.Disabled</c>) : il faut un serveur.
/// </summary>
public interface IPortalGateway
{
    Task<OperationResult<PortalLinkCreated>> CreateLinkAsync(CreatePortalLinkRequest request, CancellationToken cancellationToken);

    Task<OperationResult<IReadOnlyList<PortalLinkSummary>>> ListLinksAsync(Guid customerId, CancellationToken cancellationToken);

    Task<OperationResult<bool>> RevokeLinkAsync(Guid linkId, CancellationToken cancellationToken);

    Task<OperationResult<QuoteSignatureResponse>> GetQuoteSignatureAsync(Guid quoteId, CancellationToken cancellationToken);
}
