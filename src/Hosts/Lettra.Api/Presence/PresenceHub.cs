using Lettra.Application.Abstractions.Identity;
using Lettra.Contracts.Common;
using Lettra.Contracts.Presence;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Identity.Oidc;
using Microsoft.AspNetCore.SignalR;

namespace Lettra.Api.Presence;

/// <summary>
/// Qui est sur quel document (phase 3). Rejoindre un document exige le droit de le consulter dans son dossier : la
/// présence ne révèle rien à qui ne voit pas le document. Elle n'empêche jamais la lecture ; l'écriture reste protégée
/// par la concurrence optimiste (If-Match), le verrou d'édition n'est qu'un avertissement.
/// </summary>
internal sealed class PresenceHub(IPresenceTracker tracker, IPermissionService permissions) : Hub
{
    public async Task<DocumentPresence> Join(Guid dossierId, PresenceDocumentType documentType, Guid documentId, PresenceIntent intent)
    {
        Guid userId = await AuthorizeAsync(dossierId, documentType).ConfigureAwait(false);
        if (!Enum.IsDefined(intent))
        {
            throw new HubException(ErrorCodes.Validation.Invalid);
        }

        PresenceKey key = new(dossierId, documentType, documentId);
        await Groups.AddToGroupAsync(Context.ConnectionId, key.Group, Context.ConnectionAborted).ConfigureAwait(false);
        DocumentPresence presence = tracker.Join(key, Context.ConnectionId, userId, OidcClaims.DisplayName(Context.User!), intent);
        await Clients.OthersInGroup(key.Group).SendAsync(PresenceHubContract.Changed, presence, Context.ConnectionAborted).ConfigureAwait(false);
        return presence;
    }

    public async Task Leave(Guid dossierId, PresenceDocumentType documentType, Guid documentId)
    {
        PresenceKey key = new(dossierId, documentType, documentId);
        DocumentPresence presence = tracker.Leave(key, Context.ConnectionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, key.Group, Context.ConnectionAborted).ConfigureAwait(false);
        await Clients.Group(key.Group).SendAsync(PresenceHubContract.Changed, presence, Context.ConnectionAborted).ConfigureAwait(false);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (DocumentPresence presence in tracker.Disconnect(Context.ConnectionId))
        {
            PresenceKey key = new(presence.DossierId, presence.DocumentType, presence.DocumentId);
            await Clients.Group(key.Group).SendAsync(PresenceHubContract.Changed, presence).ConfigureAwait(false);
        }

        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    /// <summary>Droit de consulter ce type de document dans le dossier ; un dossier inconnu ou d'un autre cabinet n'en donne aucun.</summary>
    private async Task<Guid> AuthorizeAsync(Guid dossierId, PresenceDocumentType documentType)
    {
        if (Context.User is not { } user || OidcClaims.UserId(user) is not Guid userId || ReadPermission(documentType) is not Permission required)
        {
            throw new HubException(ErrorCodes.Security.Forbidden);
        }

        IReadOnlySet<Permission> granted = await permissions
            .GetEffectiveAsync(userId, new DossierId(dossierId), Context.ConnectionAborted)
            .ConfigureAwait(false);
        return granted.Contains(required) ? userId : throw new HubException(ErrorCodes.Security.Forbidden);
    }

    private static Permission? ReadPermission(PresenceDocumentType documentType) => documentType switch
    {
        PresenceDocumentType.Customer or PresenceDocumentType.Supplier => Permission.TiersConsulter,
        PresenceDocumentType.CatalogItem => Permission.CatalogueConsulter,
        PresenceDocumentType.Quote => Permission.DevisCreer,
        PresenceDocumentType.Invoice or PresenceDocumentType.CreditNote => Permission.FactureSaisir,
        _ => null,
    };
}
