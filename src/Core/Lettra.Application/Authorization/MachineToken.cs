using System.Security.Cryptography;
using System.Text;

namespace Lettra.Application.Authorization;

/// <summary>
/// Jeton partagé d'un appel de machine (collecte Prometheus, webhook d'Alertmanager), présenté en
/// <c>Authorization: Bearer</c>.
/// </summary>
public static class MachineToken
{
    private const string Scheme = "Bearer ";

    /// <summary>Compare le jeton présenté en temps constant (empreintes de même longueur).</summary>
    public static bool Matches(string authorizationHeader, string expectedToken)
    {
        ArgumentNullException.ThrowIfNull(authorizationHeader);
        ArgumentNullException.ThrowIfNull(expectedToken);
        if (!authorizationHeader.StartsWith(Scheme, StringComparison.Ordinal))
        {
            return false;
        }

        byte[] presented = SHA256.HashData(Encoding.UTF8.GetBytes(authorizationHeader[Scheme.Length..]));
        byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes(expectedToken));
        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }
}
