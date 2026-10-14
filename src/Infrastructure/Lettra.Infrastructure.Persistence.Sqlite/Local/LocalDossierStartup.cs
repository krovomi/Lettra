using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Infrastructure.Persistence.Sqlite.Local;

/// <summary>
/// Premier lancement du poste : crée le cabinet local (et sa clé dans le coffre de l'OS) et un premier dossier, puis
/// s'assure qu'un dossier est ouvert. Les dossiers suivants sont créés par le cas d'usage de création de dossier.
/// </summary>
public sealed class LocalDossierStartup(LocalDossierRegistry registry, IDossierProvisioner provisioner, TimeProvider clock)
{
    public const string FirstDossierName = "Dossier principal";

    public async Task<OrganisationId> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        LocalOrganisation organisation = await registry.GetOrCreateOrganisationAsync(clock, cancellationToken).ConfigureAwait(false);
        OrganisationId organisationId = new(organisation.Id);
        IReadOnlyList<LocalDossierManifest> dossiers = await registry.ListAsync(cancellationToken).ConfigureAwait(false);
        if (dossiers.Count == 0)
        {
            Result<DossierId> created = await provisioner.CreateAsync(organisationId, FirstDossierName, cancellationToken).ConfigureAwait(false);
            if (created.IsFailure)
            {
                throw new InvalidOperationException($"Création du premier dossier impossible : {created.Errors[0].Code}.");
            }
        }
        else if (registry.ActiveDossierId is not Guid active || dossiers.All(d => d.DossierId != active))
        {
            await registry.SetActiveAsync(new DossierId(dossiers[0].DossierId), cancellationToken).ConfigureAwait(false);
        }

        return organisationId;
    }
}
