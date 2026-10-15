using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Authorization;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Dossiers;

/// <summary>
/// Les dossiers du cabinet que l'utilisateur courant peut ouvrir (sélecteur de dossier, page d'accueil). Le profil
/// est lu une fois et les dossiers situés d'un coup, puis chacun passe par le même calcul que le pipeline.
/// </summary>
public sealed record ListAccessibleDossiersQuery : IQuery<Result<IReadOnlyList<DossierSummary>>>, IFilterByPermission
{
    public Permission Permission => Permission.DossierLire;
}

internal sealed class ListAccessibleDossiersHandler(
    ICurrentUser user,
    IAuthorizationDirectory directory,
    IDossierDirectory dossiers,
    TimeProvider clock) : IQueryHandler<ListAccessibleDossiersQuery, Result<IReadOnlyList<DossierSummary>>>
{
    public async Task<Result<IReadOnlyList<DossierSummary>>> HandleAsync(ListAccessibleDossiersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        UserAuthorizationProfile? profile = await directory.FindUserAsync(user.UserId, cancellationToken).ConfigureAwait(false);
        if (profile is not { State: AccountState.Active, Cabinet: OrganisationId cabinet })
        {
            return Array.Empty<DossierSummary>();
        }

        IReadOnlyList<DossierSummary> all = await dossiers.ListAsync(cabinet, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ScopePath> paths = await directory
            .GetDossierPathsAsync(cabinet, all.Select(d => new DossierId(d.Id)).ToArray(), cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = clock.GetUtcNow();
        HashSet<Guid> readable = paths
            .Where(p => PermissionResolver.Resolve(profile, p, now).Contains(query.Permission))
            .Select(p => p.Target.Id)
            .ToHashSet();
        return all.Where(d => readable.Contains(d.Id)).ToArray();
    }
}
