using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Authorization;
using Lettra.Contracts.Identity;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Identity;

/// <summary>
/// Émettre l'instantané de droits signé d'un poste (annexe F §4) : pour chaque dossier répliqué, les permissions de
/// données de l'utilisateur à cet instant. Aucune permission propre : le cas d'usage ne donne rien de plus que ce que
/// le registre accorde déjà, dossier par dossier, et un dossier sans droit n'apparaît pas dans l'instantané.
/// </summary>
[PublicUseCase]
public sealed record IssueRightsSnapshotCommand(Guid DeviceId, IReadOnlyList<DossierId> Dossiers) : ICommand<Result<RightsSnapshotResponse>>;

internal sealed class IssueRightsSnapshotValidator : IValidator<IssueRightsSnapshotCommand>
{
    public IReadOnlyList<Error> Validate(IssueRightsSnapshotCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        List<Error> errors = [];
        if (instance.DeviceId == Guid.Empty)
        {
            errors.Add(RightsSnapshotErrors.DeviceRequired);
        }

        if (instance.Dossiers is null || instance.Dossiers.Count > IssueRightsSnapshotRequest.MaxDossiers)
        {
            errors.Add(RightsSnapshotErrors.TooManyDossiers);
        }

        return errors;
    }
}

internal sealed class IssueRightsSnapshotHandler(
    ICurrentUser user,
    IPermissionService permissions,
    TimeProvider clock,
    IRightsSnapshotIssuer? issuer = null) : ICommandHandler<IssueRightsSnapshotCommand, Result<RightsSnapshotResponse>>
{
    public async Task<Result<RightsSnapshotResponse>> HandleAsync(IssueRightsSnapshotCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!user.IsAuthenticated)
        {
            return SecurityErrors.Unauthenticated;
        }

        if (issuer is not { IsAvailable: true })
        {
            return RightsSnapshotErrors.Unavailable;
        }

        Dictionary<DossierId, IReadOnlySet<Permission>> granted = [];
        foreach (DossierId dossier in command.Dossiers.Distinct())
        {
            IReadOnlySet<Permission> effective = await permissions.GetEffectiveAsync(user.UserId, dossier, cancellationToken).ConfigureAwait(false);

            // Hors ligne, seules les données du dossier comptent ; l'administration reste en ligne.
            HashSet<Permission> data = effective.Where(p => PermissionCatalog.KindOf(p) == PermissionKind.DossierData).ToHashSet();
            if (data.Count > 0)
            {
                granted[dossier] = data;
            }
        }

        DateTimeOffset now = clock.GetUtcNow();
        RightsSnapshot snapshot = new(user.UserId, command.DeviceId, now, now + issuer.Validity, granted);
        return new RightsSnapshotResponse(issuer.Sign(snapshot), snapshot.IssuedAt, snapshot.ExpiresAt, granted.Count);
    }
}
