using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.Common;

namespace Lettra.Application.Features.Dossiers;

/// <summary>Les dossiers du cabinet que l'utilisateur courant peut ouvrir (sélecteur de dossier, page d'accueil).</summary>
public sealed record ListAccessibleDossiersQuery : IQuery<Result<IReadOnlyList<DossierSummary>>>, IFilterByPermission
{
    public Permission Permission => Permission.DossierLire;
}

internal sealed class ListAccessibleDossiersHandler(AccessibleDossiers accessible)
    : IQueryHandler<ListAccessibleDossiersQuery, Result<IReadOnlyList<DossierSummary>>>
{
    public async Task<Result<IReadOnlyList<DossierSummary>>> HandleAsync(ListAccessibleDossiersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyList<DossierSummary> dossiers = await accessible.ListAsync(query.Permission, cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<DossierSummary>>.Success(dossiers);
    }
}
