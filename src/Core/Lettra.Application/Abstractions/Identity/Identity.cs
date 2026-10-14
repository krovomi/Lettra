using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Identity;

/// <summary>Utilisateur courant, quel que soit le fournisseur d'identité (comptes locaux, Keycloak).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid UserId { get; }
}

/// <summary>
/// Permissions élémentaires. Les rôles (annexe F) sont des ensembles de permissions à portée
/// (cabinet, groupe, dossier) ; seule la permission effective sur le dossier compte ici.
/// </summary>
public enum Permission
{
    TiersConsulter = 1,
    TiersCreer = 2,

    // Module Catalogue (chantier Tiers et Catalogue). Numérotation par plages de 10 pour éviter les collisions.
    CatalogueConsulter = 20,
    CatalogueGerer = 21,

    /// <summary>Utiliser l'assistant IA (annexe F §10). Même valeur que le catalogue des permissions de la phase 2.</summary>
    IaUtiliser = 60,
}

/// <summary>Permissions effectives d'un utilisateur sur un dossier.</summary>
public interface IPermissionService
{
    ValueTask<IReadOnlySet<Permission>> GetEffectiveAsync(Guid userId, DossierId dossier, CancellationToken cancellationToken);
}

/// <summary>
/// Toute commande et requête déclare sa permission. Le pipeline l'applique quelle que soit l'origine
/// (UI, API, synchro, IA). Un test d'architecture vérifie que chaque requête implémente cette
/// interface ou porte <see cref="PublicUseCaseAttribute"/>.
/// </summary>
public interface IRequirePermission : IDossierScoped
{
    Permission Permission { get; }
}

/// <summary>Marque explicitement un cas d'usage accessible sans permission (rare, revu en commission).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PublicUseCaseAttribute : Attribute;

public static class SecurityErrors
{
    public const string UnauthenticatedCode = "Security.Unauthenticated";
    public const string ForbiddenCode = "Security.Forbidden";

    public static Error Unauthenticated => new(UnauthenticatedCode, ErrorKind.Unauthorized);

    public static Error Forbidden => new(ForbiddenCode, ErrorKind.Forbidden);
}
