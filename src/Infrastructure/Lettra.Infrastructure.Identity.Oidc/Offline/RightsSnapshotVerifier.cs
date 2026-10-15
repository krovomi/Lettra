using System.Security.Cryptography;
using Lettra.Application.Abstractions.Identity;
using Lettra.Domain.Common;

namespace Lettra.Infrastructure.Identity.Oidc.Offline;

/// <summary>
/// Vérification d'un instantané sur le poste, sans réseau : signature par une clé publiée, émetteur attendu, poste,
/// utilisateur et période de validité. Un instantané qui échoue ne donne aucun droit (le poste se verrouille).
/// </summary>
public sealed class RightsSnapshotVerifier(IReadOnlyDictionary<string, ECDsa> keys, string issuer = "lettra")
{
    public Result<RightsSnapshot> Verify(string? token, Guid deviceId, Guid? userId, DateTimeOffset now)
    {
        Result<(RightsSnapshot Snapshot, string Issuer)> read = RightsSnapshotJws.Read(token, keys);
        if (read.IsFailure)
        {
            return Result<RightsSnapshot>.Failure(read.Errors);
        }

        (RightsSnapshot snapshot, string snapshotIssuer) = read.Value;
        if (!string.Equals(snapshotIssuer, issuer, StringComparison.Ordinal) || (userId is Guid user && snapshot.UserId != user))
        {
            return RightsSnapshotErrors.Invalid;
        }

        if (snapshot.DeviceId != deviceId)
        {
            return RightsSnapshotErrors.OtherDevice;
        }

        return snapshot.IsValidAt(now) ? snapshot : RightsSnapshotErrors.Expired;
    }
}

/// <summary>
/// Permissions du poste hors ligne : celles de l'instantané vérifié, pour l'utilisateur et les dossiers qu'il couvre,
/// tant qu'il est valide. Après son échéance, plus rien n'est permis (annexe F §4). Les portées groupe, cabinet et
/// plateforme ne sont jamais accordées hors ligne.
/// </summary>
public sealed class OfflinePermissionService(Func<RightsSnapshot?> snapshot, TimeProvider clock) : IPermissionService
{
    private static readonly IReadOnlySet<Permission> None = new HashSet<Permission>();

    public ValueTask<IReadOnlySet<Permission>> GetEffectiveAsync(Guid userId, AuthorizationScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        RightsSnapshot? current = snapshot();
        if (current is null
            || current.UserId != userId
            || scope.Kind != ScopeKind.Dossier
            || !current.IsValidAt(clock.GetUtcNow())
            || !current.Dossiers.TryGetValue(new Domain.SharedKernel.DossierId(scope.Id), out IReadOnlySet<Permission>? granted))
        {
            return ValueTask.FromResult(None);
        }

        return ValueTask.FromResult(granted);
    }
}
