using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Sync;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Sync.Server;

/// <summary>Envoi des intentions d'un poste : chacune est ensuite rejouée par son cas d'usage, avec sa propre permission.</summary>
public sealed record PushSyncIntentsCommand(DossierId Dossier, PushSyncIntentsRequest Request) : ICommand<Result<PushSyncIntentsResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>
/// Changements du dossier depuis <paramref name="Token"/>, limités à ce que l'utilisateur peut lire (tiers, catalogue).
/// <paramref name="DeviceId"/> : le poste qui appelle, s'il se nomme.
/// </summary>
public sealed record PullSyncChangesQuery(DossierId Dossier, string? Token, int Limit, Guid? DeviceId) : IQuery<Result<SyncChangesResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Séries du poste pour numéroter hors ligne (ADR-0007) : réservé à qui peut valider une facture.</summary>
public sealed record OpenDeviceSeriesCommand(DossierId Dossier, OpenDeviceSeriesRequest Request) : ICommand<Result<OpenDeviceSeriesResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureValider;
}

/// <summary>Les cas d'usage de la synchronisation côté serveur : le pipeline vérifie, <see cref="SyncServer"/> fait le travail.</summary>
internal sealed class SyncUseCases(SyncServer sync) :
    ICommandHandler<PushSyncIntentsCommand, Result<PushSyncIntentsResponse>>,
    IQueryHandler<PullSyncChangesQuery, Result<SyncChangesResponse>>,
    ICommandHandler<OpenDeviceSeriesCommand, Result<OpenDeviceSeriesResponse>>
{
    public Task<Result<PushSyncIntentsResponse>> HandleAsync(PushSyncIntentsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return sync.PushAsync(command.Request, cancellationToken);
    }

    public Task<Result<SyncChangesResponse>> HandleAsync(PullSyncChangesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return sync.PullAsync(query.Token, query.Limit, cancellationToken, query.DeviceId);
    }

    public Task<Result<OpenDeviceSeriesResponse>> HandleAsync(OpenDeviceSeriesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return sync.OpenDeviceSeriesAsync(command.Request, cancellationToken);
    }
}
