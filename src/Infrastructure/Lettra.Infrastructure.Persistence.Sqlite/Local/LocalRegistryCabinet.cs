using Lettra.Application.Abstractions.Identity;
using Lettra.Domain.SharedKernel;

namespace Lettra.Infrastructure.Persistence.Sqlite.Local;

/// <summary>
/// Le cabinet du poste est celui d'<c>organisation.json</c>, auquel le registre rattache chaque dossier. Il ne change
/// jamais une fois créé : lu une fois, puis gardé en mémoire.
/// </summary>
internal sealed class LocalRegistryCabinet(LocalDossierRegistry registry, TimeProvider clock) : ILocalCabinet
{
    private OrganisationId? _cabinet;

    public async ValueTask<OrganisationId> GetAsync(CancellationToken cancellationToken)
    {
        if (_cabinet is { } known)
        {
            return known;
        }

        LocalOrganisation organisation = await registry.GetOrCreateOrganisationAsync(clock, cancellationToken).ConfigureAwait(false);
        _cabinet = new OrganisationId(organisation.Id);
        return _cabinet.Value;
    }
}
