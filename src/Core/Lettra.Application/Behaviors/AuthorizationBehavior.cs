using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Messaging;

namespace Lettra.Application.Behaviors;

/// <summary>
/// Applique la permission déclarée par chaque cas d'usage, quelle que soit son origine (UI, API, synchro, IA),
/// sur sa portée : dossier, groupe ou cabinet. Une requête sans <see cref="ISecuredUseCase"/> doit porter
/// <see cref="PublicUseCaseAttribute"/> (vérifié par les tests d'architecture) ; à défaut elle est refusée.
/// </summary>
internal sealed class AuthorizationBehavior<TRequest, TResult>(
    ICurrentUser user,
    IPermissionService permissions) : IPipelineBehavior<TRequest, TResult>
    where TRequest : notnull
{
    public async Task<TResult> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (request is not ISecuredUseCase secured)
        {
            return Attribute.IsDefined(request.GetType(), typeof(PublicUseCaseAttribute))
                ? await next().ConfigureAwait(false)
                : ResultFactory.Failure<TResult>(SecurityErrors.Forbidden);
        }

        if (!user.IsAuthenticated)
        {
            return ResultFactory.Failure<TResult>(SecurityErrors.Unauthenticated);
        }

        // Le handler ne rend que les éléments autorisés : rien d'autre à vérifier ici.
        if (secured is IFilterByPermission)
        {
            return await next().ConfigureAwait(false);
        }

        AuthorizationScope? scope = secured switch
        {
            IRequirePermission dossier => AuthorizationScope.ForDossier(dossier.Dossier),
            IRequireGroupPermission group => AuthorizationScope.ForGroup(group.Group),
            IRequireOrganisationPermission organisation => AuthorizationScope.ForCabinet(organisation.Organisation),
            _ => null,
        };
        if (scope is null)
        {
            return ResultFactory.Failure<TResult>(SecurityErrors.Forbidden);
        }

        IReadOnlySet<Permission> granted = await permissions
            .GetEffectiveAsync(user.UserId, scope.Value, cancellationToken)
            .ConfigureAwait(false);

        return granted.Contains(secured.Permission)
            ? await next().ConfigureAwait(false)
            : ResultFactory.Failure<TResult>(SecurityErrors.Forbidden);
    }
}
