using Lettra.Application.Abstractions.Identity;
using Lettra.Domain.SharedKernel;
using Lettra.Supervision.Domain;

namespace Lettra.Supervision.Application;

/// <summary>Traduction entre la portée d'une alerte et celle des droits (annexe F §3.1).</summary>
public static class AlertScopes
{
    /// <summary>La portée sur laquelle on vérifie les droits de quelqu'un pour cette alerte.</summary>
    public static AuthorizationScope ToAuthorization(AlertScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return scope.Level switch
        {
            ScopeLevel.Dossier => AuthorizationScope.ForDossier(new DossierId(scope.Dossier!.Value)),
            ScopeLevel.Group => AuthorizationScope.ForGroup(new GroupId(scope.Group!.Value)),
            ScopeLevel.Cabinet or ScopeLevel.Device => AuthorizationScope.ForCabinet(new OrganisationId(scope.Cabinet!.Value)),
            _ => AuthorizationScope.Plateforme,
        };
    }

    /// <summary>Vrai si l'alerte se situe dans la portée consultée : on n'agit pas hors du périmètre vérifié.</summary>
    public static bool IsWithin(AlertScope alert, AuthorizationScope within)
    {
        ArgumentNullException.ThrowIfNull(alert);
        return within.Kind switch
        {
            ScopeKind.Cabinet => alert.Cabinet == within.Id,
            ScopeKind.Groupe => alert.Group == within.Id,
            ScopeKind.Dossier => alert.Dossier == within.Id,
            ScopeKind.Plateforme => alert.IsTechnical,
            _ => false,
        };
    }
}
