using System.Security.Cryptography;
using Lettra.Infrastructure.Persistence.Security;

namespace Lettra.Persistence.Tests;

public sealed class KeyCacheTests
{
    [Fact]
    public void Rend_une_copie_que_l_appelant_peut_effacer()
    {
        using KeyCache<int> cache = new(capacity: 4, lifetime: TimeSpan.FromMinutes(1));
        byte[] secret = [1, 2, 3];
        cache.Set(1, secret);
        CryptographicOperations.ZeroMemory(secret);

        byte[] first = cache.Find(1)!;
        first.Should().Equal(1, 2, 3);
        CryptographicOperations.ZeroMemory(first);

        cache.Find(1).Should().Equal(1, 2, 3);
        cache.Find(2).Should().BeNull();
    }

    [Fact]
    public void Oublie_une_cle_retiree()
    {
        using KeyCache<int> cache = new(capacity: 4, lifetime: TimeSpan.FromMinutes(1));
        cache.Set(1, [9]);

        cache.Remove(1);

        cache.Find(1).Should().BeNull();
    }

    [Fact]
    public async Task Une_cle_expiree_n_est_plus_rendue()
    {
        using KeyCache<int> cache = new(capacity: 4, lifetime: TimeSpan.FromMilliseconds(30));
        cache.Set(1, [9]);

        await Task.Delay(120);

        cache.Find(1).Should().BeNull();
    }

    [Fact]
    public void Reglages_invalides_refuses()
    {
        Action noCapacity = () => _ = new KeyCache<int>(0, TimeSpan.FromMinutes(1));
        Action noLifetime = () => _ = new KeyCache<int>(1, TimeSpan.Zero);

        noCapacity.Should().Throw<ArgumentOutOfRangeException>();
        noLifetime.Should().Throw<ArgumentOutOfRangeException>();
    }
}
