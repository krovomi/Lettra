using Lettra.Contracts.Common;
using Lettra.Contracts.Portal;
using Lettra.Exports.Application.Portal;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>
/// Mêmes cas d'usage que les routes <c>/portal-links</c> de l'API. Sur le poste, l'espace client est fermé : la création
/// d'un lien rend <c>Portal.Disabled</c> (le client ne peut pas joindre un poste).
/// </summary>
internal sealed class InProcessPortalGateway(IServiceScopeFactory scopes) : IPortalGateway
{
    public async Task<OperationResult<PortalLinkCreated>> CreateLinkAsync(CreatePortalLinkRequest request, CancellationToken cancellationToken)
    {
        OperationResult<PortalLinkResponse> created = await InProcessScope.RunAsync(
            scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new CreatePortalLinkCommand(dossier, request), ct), cancellationToken).ConfigureAwait(false);
        return created.IsSuccess
            ? OperationResult<PortalLinkCreated>.Success(new PortalLinkCreated(created.Value!, created.Value!.Path))
            : OperationResult<PortalLinkCreated>.Failure(created.Errors);
    }

    public Task<OperationResult<IReadOnlyList<PortalLinkSummary>>> ListLinksAsync(Guid customerId, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new ListPortalLinksQuery(dossier, customerId), ct), cancellationToken);

    public Task<OperationResult<bool>> RevokeLinkAsync(Guid linkId, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new RevokePortalLinkCommand(dossier, linkId), ct), cancellationToken);

    public Task<OperationResult<QuoteSignatureResponse>> GetQuoteSignatureAsync(Guid quoteId, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetQuoteSignatureQuery(dossier, quoteId), ct), cancellationToken);
}
