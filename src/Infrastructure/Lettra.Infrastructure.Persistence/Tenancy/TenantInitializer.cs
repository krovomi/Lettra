using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Authorization;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Infrastructure.Persistence.Tenancy;

/// <summary>
/// Résout le dossier demandé (en-tête <c>X-Dossier</c> ou dossier ouvert sur le poste), le cherche dans le
/// catalogue et vérifie que l'utilisateur a au moins une permission qui ouvre ses données (annexe F §4 :
/// la base n'est ouverte qu'après l'autorisation). Un dossier inconnu et un dossier interdit donnent la
/// même réponse, pour ne pas révéler son existence.
/// </summary>
internal sealed class TenantInitializer(
    IRequestedDossierSource source,
    ITenantCatalog catalog,
    ICurrentUser user,
    IPermissionService permissions,
    TenantContext context) : ITenantInitializer
{
    public async Task<Result> InitializeAsync(CancellationToken cancellationToken)
    {
        if (context.IsResolved)
        {
            return Result.Success();
        }

        if (source.RequestedDossier is not DossierId dossierId)
        {
            return TenancyErrors.DossierRequired;
        }

        if (!user.IsAuthenticated)
        {
            return SecurityErrors.Unauthenticated;
        }

        TenantDescriptor? descriptor = await catalog.FindAsync(dossierId, cancellationToken).ConfigureAwait(false);
        if (descriptor is null)
        {
            return TenancyErrors.DossierNotFound;
        }

        IReadOnlySet<Permission> granted = await permissions.GetEffectiveAsync(user.UserId, dossierId, cancellationToken).ConfigureAwait(false);
        if (!PermissionCatalog.OpensDossierData(granted))
        {
            return TenancyErrors.DossierNotFound;
        }

        context.Set(descriptor);
        return Result.Success();
    }
}
