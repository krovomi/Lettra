using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Sync;
using Lettra.Contracts.Sync;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Sync;

/// <summary>Les postes qui se sont synchronisés avec le dossier, du plus récent contact au plus ancien (étape 4.4).</summary>
public sealed record ListSyncDevicesQuery(DossierId Dossier) : IQuery<Result<IReadOnlyList<SyncDeviceResponse>>>, IRequirePermission
{
    public Permission Permission => Permission.PostesGerer;
}

/// <summary>
/// Révoquer un poste (perdu, volé, collaborateur parti) : il est refusé dès son prochain contact, qui lui fait effacer
/// sa réplique. Ce qu'il n'a pas encore envoyé est perdu ; c'est le prix d'un poste qu'on ne contrôle plus.
/// </summary>
public sealed record RevokeSyncDeviceCommand(DossierId Dossier, Guid DeviceId) : ICommand<Result>, IRequirePermission
{
    public Permission Permission => Permission.PostesGerer;
}

/// <summary>Règle des postes du dossier : durée maximale hors ligne au-delà de laquelle un poste ne valide plus.</summary>
public sealed record GetSyncPolicyQuery(DossierId Dossier) : IQuery<Result<SyncPolicyResponse>>, IRequirePermission
{
    public Permission Permission => Permission.PostesGerer;
}

public sealed record SaveSyncPolicyCommand(DossierId Dossier, SaveSyncPolicyRequest Request) : ICommand<Result<SyncPolicyResponse>>, IRequirePermission
{
    public Permission Permission => Permission.PostesGerer;
}

/// <summary>
/// Débloquer la série d'un poste : la pièce attendue sous son prochain numéro, refusée à la réception (avoir sans
/// approbation, droit retiré, règle changée), sera reçue à son prochain envoi sous l'autorité de qui débloque. Les
/// pièces suivantes, qui l'attendaient, suivent. Une pièce altérée reste refusée : l'empreinte est toujours revérifiée.
/// </summary>
public sealed record ReleaseDeviceSeriesCommand(DossierId Dossier, Guid DeviceId) : ICommand<Result<ReleaseDeviceSeriesResponse>>, IRequirePermission
{
    public Permission Permission => Permission.PostesGerer;
}

internal sealed class SaveSyncPolicyValidator : IValidator<SaveSyncPolicyCommand>
{
    public IReadOnlyList<Error> Validate(SaveSyncPolicyCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return instance.Request is { } request && OfflinePolicy.IsValid(request.MaxOfflineHours) ? [] : [SyncErrors.PolicyInvalid];
    }
}

internal sealed class SyncDeviceHandlers(ISyncDeviceRegistry devices, ICurrentUser user, TimeProvider clock) :
    IQueryHandler<ListSyncDevicesQuery, Result<IReadOnlyList<SyncDeviceResponse>>>,
    ICommandHandler<RevokeSyncDeviceCommand, Result>,
    IQueryHandler<GetSyncPolicyQuery, Result<SyncPolicyResponse>>,
    ICommandHandler<SaveSyncPolicyCommand, Result<SyncPolicyResponse>>,
    ICommandHandler<ReleaseDeviceSeriesCommand, Result<ReleaseDeviceSeriesResponse>>
{
    public Task<Result<ReleaseDeviceSeriesResponse>> HandleAsync(ReleaseDeviceSeriesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return devices.ReleaseNextAsync(command.DeviceId, user.UserId, clock.GetUtcNow(), cancellationToken);
    }

    public async Task<Result<IReadOnlyList<SyncDeviceResponse>>> HandleAsync(ListSyncDevicesQuery query, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<SyncDeviceResponse>>.Success(await devices.ListAsync(cancellationToken).ConfigureAwait(false));

    public async Task<Result> HandleAsync(RevokeSyncDeviceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await devices.RevokeAsync(command.DeviceId, user.UserId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false)
            ? Result.Success()
            : SyncErrors.DeviceUnknown;
    }

    public async Task<Result<SyncPolicyResponse>> HandleAsync(GetSyncPolicyQuery query, CancellationToken cancellationToken) =>
        new SyncPolicyResponse(await devices.GetMaxOfflineHoursAsync(cancellationToken).ConfigureAwait(false));

    public async Task<Result<SyncPolicyResponse>> HandleAsync(SaveSyncPolicyCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await devices.SaveMaxOfflineHoursAsync(command.Request.MaxOfflineHours, cancellationToken).ConfigureAwait(false);
        return new SyncPolicyResponse(command.Request.MaxOfflineHours);
    }
}
