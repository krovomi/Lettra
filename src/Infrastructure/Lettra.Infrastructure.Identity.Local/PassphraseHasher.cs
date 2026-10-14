using System.Security.Cryptography;
using System.Text;

namespace Lettra.Infrastructure.Identity.Local;

/// <summary>
/// Hachage de la phrase secrète : PBKDF2-SHA512 du runtime .NET (aucune dépendance), sel aléatoire de 32 octets,
/// comparaison en temps constant. Les itérations sont stockées avec le hachage pour pouvoir les relever.
/// </summary>
internal static class PassphraseHasher
{
    private const int SaltSize = 32;
    private const int HashSize = 64;

    public static (byte[] Salt, byte[] Hash) Hash(string passphrase, int iterations)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        return (salt, Derive(passphrase, salt, iterations));
    }

    public static bool Verify(string passphrase, byte[] salt, byte[] expected, int iterations) =>
        CryptographicOperations.FixedTimeEquals(Derive(passphrase, salt, iterations), expected);

    private static byte[] Derive(string passphrase, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase.Normalize(NormalizationForm.FormC)), salt, iterations, HashAlgorithmName.SHA512, HashSize);
}
