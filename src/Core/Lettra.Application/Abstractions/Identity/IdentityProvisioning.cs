using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Identity;

/// <summary>Un compte chez le fournisseur d'identité, et si l'e-mail pour choisir son mot de passe est parti.</summary>
public sealed record ProvisionedAccount(Guid UserId, bool InvitationSent);

/// <summary>
/// Les cabinets et leurs comptes chez le fournisseur d'identité (organisations Keycloak). Chaque opération est
/// idempotente : une arrivée interrompue se relance telle quelle, sans doublon.
/// </summary>
public interface IIdentityProvisioning
{
    /// <summary><c>false</c> quand aucun fournisseur d'identité n'est réglé pour la plateforme.</summary>
    bool IsConfigured { get; }

    /// <summary>Crée l'organisation du cabinet, ou retrouve celle qui porte déjà ce nom : son identifiant est celui du cabinet.</summary>
    Task<Result<OrganisationId>> EnsureOrganisationAsync(string alias, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Crée le compte (ou retrouve celui de cette adresse), le rattache au cabinet et lui envoie l'e-mail pour choisir
    /// son mot de passe ; le second facteur est enrôlé à la première connexion.
    /// </summary>
    Task<Result<ProvisionedAccount>> EnsureMemberAsync(OrganisationId organisation, string email, string displayName, CancellationToken cancellationToken);

    /// <summary>Supprime les comptes du cabinet puis son organisation (sans erreur si elle n'existe déjà plus).</summary>
    Task DeleteOrganisationAsync(OrganisationId organisation, CancellationToken cancellationToken);
}
