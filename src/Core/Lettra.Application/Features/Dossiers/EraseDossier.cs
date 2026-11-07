using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Dossiers;

/// <summary>
/// Demander l'effacement d'un dossier du cabinet : il est fermé à tous aussitôt, l'effacement reste annulable pendant le
/// délai de rétractation, puis sa clé est détruite et sa base supprimée. Réservé à l'administrateur du cabinet.
/// </summary>
public sealed record ScheduleDossierErasureCommand(OrganisationId Organisation, DossierId Dossier)
    : ICommand<Result<DossierErasureResponse>>, IRequireOrganisationPermission
{
    public Permission Permission => Permission.CabinetAdministrer;
}

/// <summary>Annuler l'effacement demandé d'un dossier, tant que le délai de rétractation court : il rouvre tel quel.</summary>
public sealed record CancelDossierErasureCommand(OrganisationId Organisation, DossierId Dossier)
    : ICommand<Result<DossierSummary>>, IRequireOrganisationPermission
{
    public Permission Permission => Permission.CabinetAdministrer;
}

/// <summary>Les dossiers du cabinet dont l'effacement est demandé, la fin du délai de rétractation la plus proche d'abord.</summary>
public sealed record ListScheduledDossierErasuresQuery(OrganisationId Organisation)
    : IQuery<Result<IReadOnlyList<DossierErasureResponse>>>, IRequireOrganisationPermission
{
    public Permission Permission => Permission.CabinetAdministrer;
}

internal sealed class ScheduleDossierErasureHandler(IDossierEraser eraser, ICurrentUser user)
    : ICommandHandler<ScheduleDossierErasureCommand, Result<DossierErasureResponse>>
{
    public Task<Result<DossierErasureResponse>> HandleAsync(ScheduleDossierErasureCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return eraser.ScheduleAsync(command.Organisation, command.Dossier, user.UserId, cancellationToken);
    }
}

internal sealed class CancelDossierErasureHandler(IDossierEraser eraser)
    : ICommandHandler<CancelDossierErasureCommand, Result<DossierSummary>>
{
    public Task<Result<DossierSummary>> HandleAsync(CancelDossierErasureCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return eraser.CancelAsync(command.Organisation, command.Dossier, cancellationToken);
    }
}

internal sealed class ListScheduledDossierErasuresHandler(IDossierEraser eraser)
    : IQueryHandler<ListScheduledDossierErasuresQuery, Result<IReadOnlyList<DossierErasureResponse>>>
{
    public async Task<Result<IReadOnlyList<DossierErasureResponse>>> HandleAsync(ListScheduledDossierErasuresQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Result<IReadOnlyList<DossierErasureResponse>>.Success(
            await eraser.ListScheduledAsync(query.Organisation, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Poste et hôtes sans catalogue : aucun dossier ne s'efface par l'API (la base appartient à qui l'a déclarée).</summary>
internal sealed class NoDossierEraser : IDossierEraser
{
    public Task<Result<DossierErasureResponse>> ScheduleAsync(OrganisationId organisation, DossierId dossier, Guid requestedBy, CancellationToken cancellationToken) =>
        Task.FromResult<Result<DossierErasureResponse>>(DossierErrors.Declared);

    public Task<Result<DossierSummary>> CancelAsync(OrganisationId organisation, DossierId dossier, CancellationToken cancellationToken) =>
        Task.FromResult<Result<DossierSummary>>(DossierErrors.ErasureNotCancellable);

    public Task<IReadOnlyList<DossierErasureResponse>> ListScheduledAsync(OrganisationId organisation, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DossierErasureResponse>>([]);
}
