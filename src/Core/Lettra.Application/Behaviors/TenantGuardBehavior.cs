using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Messaging;

namespace Lettra.Application.Behaviors;

/// <summary>
/// Garde-fou multi-tenant : une requête qui vise un dossier ne s'exécute que si ce dossier est
/// celui dont la base est ouverte. Aucune requête ne peut atteindre la base d'un autre dossier.
/// </summary>
internal sealed class TenantGuardBehavior<TRequest, TResult>(ITenantContext tenant) : IPipelineBehavior<TRequest, TResult>
    where TRequest : notnull
{
    public Task<TResult> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (request is not IDossierScoped scoped)
        {
            return next();
        }

        if (!tenant.IsResolved)
        {
            return Task.FromResult(ResultFactory.Failure<TResult>(TenancyErrors.DossierRequired));
        }

        return tenant.Current.DossierId == scoped.Dossier
            ? next()
            : Task.FromResult(ResultFactory.Failure<TResult>(TenancyErrors.DossierMismatch));
    }
}
