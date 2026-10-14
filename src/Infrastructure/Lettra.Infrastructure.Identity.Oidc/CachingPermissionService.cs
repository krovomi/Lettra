using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Authorization;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Identity.Oidc;

/// <summary>
/// Cache court des permissions effectives (HybridCache : mémoire, et Redis quand la cellule le fournit),
/// invalidé par utilisateur ou en bloc sur <c>DroitsModifies</c>. Un retrait de droit prend effet à
/// l'invalidation, et au plus tard à l'expiration (5 minutes maximum).
/// </summary>
internal sealed class CachingPermissionService(
    DirectoryPermissionService inner,
    HybridCache cache,
    IOptions<OidcOptions> options) : IPermissionService, IPermissionCacheInvalidator
{
    private const string AllTag = "lettra-perm";

    public async ValueTask<IReadOnlySet<Permission>> GetEffectiveAsync(Guid userId, AuthorizationScope scope, CancellationToken cancellationToken)
    {
        TimeSpan duration = options.Value.PermissionCacheDuration < OidcOptions.MaxPermissionCacheDuration
            ? options.Value.PermissionCacheDuration
            : OidcOptions.MaxPermissionCacheDuration;
        HybridCacheEntryOptions entry = new() { Expiration = duration, LocalCacheExpiration = duration };

        int[] granted = await cache.GetOrCreateAsync(
            $"lettra:perm:{userId:N}:{(int)scope.Kind}:{scope.Id:N}",
            (inner, userId, scope),
            static async (state, ct) =>
                (await state.inner.GetEffectiveAsync(state.userId, state.scope, ct).ConfigureAwait(false)).Select(p => (int)p).ToArray(),
            entry,
            [AllTag, UserTag(userId)],
            cancellationToken).ConfigureAwait(false);

        return granted.Length == 0 ? PermissionResolver.None : granted.Select(p => (Permission)p).ToHashSet();
    }

    public ValueTask InvalidateUserAsync(Guid userId, CancellationToken cancellationToken) =>
        cache.RemoveByTagAsync(UserTag(userId), cancellationToken);

    public ValueTask InvalidateAllAsync(CancellationToken cancellationToken) =>
        cache.RemoveByTagAsync(AllTag, cancellationToken);

    private static string UserTag(Guid userId) => $"lettra-perm-user-{userId:N}";
}
