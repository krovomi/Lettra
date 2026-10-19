using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Infrastructure.Persistence.Sqlite.Local;

/// <summary>
/// Premier lancement du poste : crée le cabinet local (et sa clé dans le coffre de l'OS) et un premier dossier, puis
/// s'assure qu'un dossier est ouvert. Les dossiers suivants sont créés par le cas d'usage de création de dossier.
/// Poste relié (mode 4) : seulement la réplique du dossier du serveur.
/// </summary>
public sealed class LocalDossierStartup
{
    public const string FirstDossierName = "Dossier principal";

    /// <summary>Nom de la réplique jusqu'à ce que la synchronisation apporte celui du serveur.</summary>
    public const string ReplicaName = "Dossier du cabinet";

    private readonly LocalDossierRegistry _registry;
    private readonly LocalDossierProvisioner _provisioner;
    private readonly TimeProvider _clock;

    internal LocalDossierStartup(LocalDossierRegistry registry, LocalDossierProvisioner provisioner, TimeProvider clock)
    {
        _registry = registry;
        _provisioner = provisioner;
        _clock = clock;
    }

    public async Task<OrganisationId> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        LocalOrganisation organisation = await _registry.GetOrCreateOrganisationAsync(_clock, cancellationToken).ConfigureAwait(false);
        OrganisationId organisationId = new(organisation.Id);
        IReadOnlyList<LocalDossierManifest> dossiers = await _registry.ListAsync(cancellationToken).ConfigureAwait(false);
        if (dossiers.Count == 0)
        {
            Result<DossierId> created = await _provisioner.CreateAsync(organisationId, FirstDossierName, cancellationToken).ConfigureAwait(false);
            if (created.IsFailure)
            {
                throw new InvalidOperationException($"Création du premier dossier impossible : {created.Errors[0].Code}.");
            }
        }
        else if (_registry.ActiveDossierId is not Guid active || dossiers.All(d => d.DossierId != active))
        {
            await _registry.SetActiveAsync(new DossierId(dossiers[0].DossierId), cancellationToken).ConfigureAwait(false);
        }

        return organisationId;
    }

    /// <summary>
    /// Poste relié (mode 4) : la réplique du dossier du serveur, créée au premier lancement avec l'identifiant du
    /// serveur (clé du cabinet local dans le coffre de l'OS, base chiffrée), puis ouverte. Aucun autre dossier n'est créé.
    /// </summary>
    public async Task<OrganisationId> EnsureReplicaReadyAsync(DossierId serverDossier, CancellationToken cancellationToken)
    {
        LocalOrganisation organisation = await _registry.GetOrCreateOrganisationAsync(_clock, cancellationToken).ConfigureAwait(false);
        OrganisationId organisationId = new(organisation.Id);
        if (await _registry.FindAsync(serverDossier.Value, cancellationToken).ConfigureAwait(false) is null)
        {
            Result<DossierId> created = await _provisioner.CreateAsync(organisationId, serverDossier, ReplicaName, cancellationToken).ConfigureAwait(false);
            if (created.IsFailure)
            {
                throw new InvalidOperationException($"Création de la réplique impossible : {created.Errors[0].Code}.");
            }
        }

        await _registry.SetActiveAsync(serverDossier, cancellationToken).ConfigureAwait(false);
        return organisationId;
    }
}
