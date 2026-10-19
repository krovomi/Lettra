using System.Globalization;
using System.Text;
using Lettra.Contracts.Identity;

namespace Lettra.UI.Shared.Services;

/// <summary>
/// Suffixe des clés retenues sur l'appareil pour un utilisateur (tutoriels vus, menu réglé) : empreinte de son nom et du
/// mode de session, pour que deux personnes sur le même poste aient chacune leurs réglages, sans écrire leur nom en clair.
/// </summary>
public static class UserStorageKey
{
    /// <summary>Identité retenue d'une session ouverte ; <c>null</c> sans session.</summary>
    public static string? UserOf(SessionResponse? session) =>
        session is { IsAuthenticated: true } response ? $"{response.Mode}|{response.DisplayName}" : null;

    /// <summary>Empreinte FNV-1a sur 32 bits, en hexadécimal (« 811c9dc5 » sans utilisateur).</summary>
    public static string Hash(string? user)
    {
        uint hash = 2166136261;
        foreach (byte b in Encoding.UTF8.GetBytes(user ?? string.Empty))
        {
            hash = (hash ^ b) * 16777619;
        }

        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }
}
