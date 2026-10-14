using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Messaging;

namespace Lettra.Application.Behaviors;

/// <summary>
/// Applique la permission déclarée par chaque cas d'usage, quelle que soit son origine (UI, API, synchro, IA).
/// Une requête sans <see cref="IRequirePermission"/> doit porter <see cref="PublicUseCaseAttribute"/>
/// (vérifié par les tests d'architecture) ; à défaut elle est refusée.
/// </summary>
internal sealed class AuthorizationBehavior<TRequest, TResult>(
    ICurrentUser user,
    IPermissionService permissions) : IPipelineBehavior<TRequest, TResult>
    where TRequest : notnull
{
    public async Task<TResult> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (request is not IRequirePermission secured)
        {
            return Attribute.IsDefined(request.GetType(), typeof(PublicUseCaseAttribute))
                ? await next().ConfigureAwait(false)
                : ResultFactory.Failure<TResult>(SecurityErrors.Forbidden);
        }

        if (!user.IsAuthenticated)
        {
            return ResultFactory.Failure<TResult>(SecurityErrors.Unauthenticated);
        }

        IReadOnlySet<Permission> granted = await permissions
            .GetEffectiveAsync(user.UserId, secured.Dossier, cancellationToken)
            .ConfigureAwait(false);

        return granted.Contains(secured.Permission)
            ? await next().ConfigureAwait(false)
            : ResultFactory.Failure<TResult>(SecurityErrors.Forbidden);
    }
}
