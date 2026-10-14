using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;

namespace Lettra.Api.Security;

/// <summary>
/// Sessions du BFF côté serveur (annexe F §2) : le cookie du navigateur ne contient qu'une clé aléatoire ; les
/// jetons Keycloak restent dans le cache distribué (Redis de la cellule), chiffrés par Data Protection.
/// </summary>
internal sealed class DistributedTicketStore(IDistributedCache cache, IDataProtectionProvider protection, TimeProvider clock) : ITicketStore
{
    private const string Prefix = "lettra-bff:";
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(10);
    private readonly IDataProtector _protector = protection.CreateProtector("Lettra.Bff.Ticket");

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        string key = Prefix + Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        await RenewAsync(key, ticket).ConfigureAwait(false);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        DateTimeOffset expires = ticket.Properties.ExpiresUtc ?? clock.GetUtcNow() + DefaultLifetime;
        byte[] payload = _protector.Protect(TicketSerializer.Default.Serialize(ticket));
        return cache.SetAsync(key, payload, new DistributedCacheEntryOptions { AbsoluteExpiration = expires });
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        byte[]? payload = await cache.GetAsync(key).ConfigureAwait(false);
        if (payload is null)
        {
            return null;
        }

        try
        {
            return TicketSerializer.Default.Deserialize(_protector.Unprotect(payload));
        }
        catch (CryptographicException)
        {
            // Clé de chiffrement tournée ou entrée altérée : la session est perdue, l'utilisateur se reconnecte.
            return null;
        }
    }

    public Task RemoveAsync(string key) => cache.RemoveAsync(key);

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal static class CookieTicketStoreSetup
{
    public static void Use(CookieAuthenticationOptions options, ITicketStore store) => options.SessionStore = store;
}
