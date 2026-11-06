using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace Lettra.Infrastructure.Persistence.Security;

/// <summary>
/// Cache borné de clés en clair (KEK d'un cabinet, DEK d'un dossier) : quelques minutes au plus, un nombre d'entrées
/// limité, et chaque clé effacée de la mémoire à son éviction. Il rend toujours une copie, que l'appelant peut effacer
/// sans toucher au cache. Un KMS ou un catalogue brièvement injoignable ne ferme pas aussitôt les dossiers ouverts.
/// </summary>
public sealed class KeyCache<TKey> : IDisposable
    where TKey : notnull
{
    private readonly MemoryCache _cache;
    private readonly TimeSpan _lifetime;

    public KeyCache(int capacity, TimeSpan lifetime)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        _lifetime = lifetime;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = capacity });
    }

    public byte[]? Find(TKey key) => _cache.TryGetValue(key, out byte[]? value) && value is not null ? [.. value] : null;

    /// <summary>Garde une copie de <paramref name="secret"/> ; l'appelant reste maître (et chargé de l'effacement) de l'original.</summary>
    public void Set(TKey key, ReadOnlySpan<byte> secret)
    {
        MemoryCacheEntryOptions options = new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = _lifetime }
            .RegisterPostEvictionCallback(static (_, value, _, _) =>
            {
                if (value is byte[] evicted)
                {
                    CryptographicOperations.ZeroMemory(evicted);
                }
            });
        _cache.Set(key, secret.ToArray(), options);
    }

    /// <summary>Oublie une clé (détruite, tournée) : la copie en cache est effacée.</summary>
    public void Remove(TKey key) => _cache.Remove(key);

    public void Dispose() => _cache.Dispose();
}
