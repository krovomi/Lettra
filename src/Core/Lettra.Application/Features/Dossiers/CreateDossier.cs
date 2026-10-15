using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Dossiers;

/// <summary>
/// Créer un dossier client du cabinet : sa base, sa clé et son inscription au registre. Cas d'usage d'administration
/// du cabinet, hors de tout dossier ; la permission est vérifiée sur le cabinet.
/// </summary>
public sealed record CreateDossierCommand(OrganisationId Organisation, string Name)
    : ICommand<Result<DossierSummary>>, IRequireOrganisationPermission
{
    public Permission Permission => Permission.DossiersCreer;
}

internal sealed class CreateDossierHandler(IDossierProvisioner provisioner) : ICommandHandler<CreateDossierCommand, Result<DossierSummary>>
{
    public async Task<Result<DossierSummary>> HandleAsync(CreateDossierCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        string name = command.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return DossierErrors.NameRequired;
        }

        if (name.Length > CreateDossierRequest.MaxNameLength)
        {
            return DossierErrors.NameTooLong;
        }

        Result<DossierId> created = await provisioner.CreateAsync(command.Organisation, name, cancellationToken).ConfigureAwait(false);
        return created.IsSuccess
            ? new DossierSummary(created.Value.Value, name)
            : Result<DossierSummary>.Failure(created.Errors);
    }
}
