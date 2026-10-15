using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Identity;

/// <summary>
/// Utilisateur courant, quel que soit le fournisseur d'identité (compte local, Keycloak). Le jeton dit
/// <b>qui</b> est l'utilisateur, jamais ce qu'il peut faire : les droits viennent d'<see cref="IPermissionService"/>.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid UserId { get; }
}

/// <summary>
/// Permissions effectives d'un utilisateur sur une portée (dossier, groupe, cabinet, plateforme), calculées
/// à partir de ses attributions (annexe F §3.2 et §4). Un compte non actif n'a aucune permission.
/// </summary>
public interface IPermissionService
{
    ValueTask<IReadOnlySet<Permission>> GetEffectiveAsync(Guid userId, AuthorizationScope scope, CancellationToken cancellationToken);
}

/// <summary>Invalide les permissions mises en cache, sur l'événement <c>DroitsModifies</c> du catalogue.</summary>
public interface IPermissionCacheInvalidator
{
    /// <summary>Les droits d'un utilisateur ont changé (attribution, groupe, état du compte).</summary>
    ValueTask InvalidateUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Un changement touche beaucoup d'utilisateurs (rôle personnalisé modifié, dossier déplacé de groupe).</summary>
    ValueTask InvalidateAllAsync(CancellationToken cancellationToken);
}

public static class PermissionServiceExtensions
{
    public static ValueTask<IReadOnlySet<Permission>> GetEffectiveAsync(
        this IPermissionService service, Guid userId, DossierId dossier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        return service.GetEffectiveAsync(userId, AuthorizationScope.ForDossier(dossier), cancellationToken);
    }
}

/// <summary>Un cas d'usage protégé : il déclare la permission qu'il exige. Sa portée vient de l'interface dérivée.</summary>
public interface ISecuredUseCase
{
    Permission Permission { get; }
}

/// <summary>
/// Toute commande et requête déclare sa permission. Le pipeline l'applique quelle que soit l'origine
/// (UI, API, synchro, IA). Un test d'architecture vérifie que chaque requête implémente une interface
/// <see cref="ISecuredUseCase"/> ou porte <see cref="PublicUseCaseAttribute"/>.
/// </summary>
public interface IRequirePermission : ISecuredUseCase, IDossierScoped;

/// <summary>Cas d'usage d'administration du cabinet (utilisateurs, groupes, politiques), hors de tout dossier.</summary>
public interface IRequireOrganisationPermission : ISecuredUseCase
{
    OrganisationId Organisation { get; }
}

/// <summary>Cas d'usage portant sur un groupe du cabinet (membres, rattachement des dossiers).</summary>
public interface IRequireGroupPermission : ISecuredUseCase
{
    GroupId Group { get; }
}

/// <summary>
/// Cas d'usage qui parcourt plusieurs portées et ne rend que celles où l'utilisateur a la permission (liste des
/// dossiers qu'il peut ouvrir) : le pipeline exige seulement l'authentification, le handler filtre chaque élément.
/// </summary>
public interface IFilterByPermission : ISecuredUseCase;

/// <summary>Marque explicitement un cas d'usage accessible sans permission (rare, revu en commission).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PublicUseCaseAttribute : Attribute;

public static class SecurityErrors
{
    public const string UnauthenticatedCode = "Security.Unauthenticated";
    public const string ForbiddenCode = "Security.Forbidden";
    public const string MfaRequiredCode = "Security.MfaRequired";
    public const string CsrfHeaderMissingCode = "Security.CsrfHeaderMissing";

    public static Error Unauthenticated => new(UnauthenticatedCode, ErrorKind.Unauthorized);

    public static Error Forbidden => new(ForbiddenCode, ErrorKind.Forbidden);

    /// <summary>Authentifié sans second facteur alors que la politique l'exige (annexe F §2).</summary>
    public static Error MfaRequired => new(MfaRequiredCode, ErrorKind.Unauthorized);

    /// <summary>Appel du navigateur (cookie du BFF) sans l'en-tête anti-CSRF.</summary>
    public static Error CsrfHeaderMissing => new(CsrfHeaderMissingCode, ErrorKind.Forbidden);
}
