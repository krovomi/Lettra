using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.SharedKernel;

namespace Lettra.Infrastructure.Persistence.Security;

/// <summary>
/// Clés de la chaîne anti-fraude, dérivées (HKDF-SHA256) de la clé maîtresse du cabinet, comme les clés des dossiers
/// sont enveloppées par elle (ADR-0011) : aucune clé de la chaîne n'est stockée, ni en base ni ailleurs. Sur le poste,
/// la KEK vient du coffre de l'OS ; sur un serveur, du coffre de la cellule. La cible de production (annexe F) remplace
/// cette dérivation par une clé de signature non exportable dans un HSM / KMS, derrière le même port.
/// </summary>
internal sealed class DerivedIntegrityKeyring(IKekStore kekStore) : IIntegrityKeyring
{
    private const string SealInfo = "lettra/integrite/sceau/v1";
    private const string SigningInfo = "lettra/integrite/signature/v1";

    private readonly ConcurrentDictionary<OrganisationId, IIntegrityKeys> _cache = new();

    public async ValueTask<Result<IIntegrityKeys>> GetAsync(OrganisationId cabinet, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(cabinet, out IIntegrityKeys? cached))
        {
            return Result<IIntegrityKeys>.Success(cached);
        }

        byte[]? kek = await kekStore.FindAsync(cabinet, cancellationToken).ConfigureAwait(false);
        if (kek is null)
        {
            return IntegrityErrors.KeyUnavailable;
        }

        byte[] salt = cabinet.Value.ToByteArray();
        byte[] sealKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, kek, 32, salt, Encoding.ASCII.GetBytes(SealInfo));
        ECDsa signing = DeriveSigningKey(kek, salt);
        return Result<IIntegrityKeys>.Success(_cache.GetOrAdd(cabinet, new CabinetIntegrityKeys(sealKey, signing)));
    }

    /// <summary>Clé privée P-256 déterministe : la même KEK redonne toujours la même clé, donc le même identifiant.</summary>
    private static ECDsa DeriveSigningKey(byte[] kek, byte[] salt)
    {
        for (int counter = 0; ; counter++)
        {
            byte[] scalar = HKDF.DeriveKey(HashAlgorithmName.SHA256, kek, 32, salt, Encoding.ASCII.GetBytes($"{SigningInfo}/{counter}"));
            try
            {
                return ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = scalar });
            }
            catch (CryptographicException) when (counter < 8)
            {
                // Scalaire hors de l'ordre de la courbe (probabilité ~2^-32) : on dérive le suivant.
            }
            finally
            {
                CryptographicOperations.ZeroMemory(scalar);
            }
        }
    }
}

/// <summary>Sceau HMAC-SHA256 des entrées et signature ECDSA P-256 des clôtures, pour un cabinet.</summary>
internal sealed class CabinetIntegrityKeys : IIntegrityKeys
{
    private readonly byte[] _sealKey;
    private readonly ECDsa _signing;

    public CabinetIntegrityKeys(byte[] sealKey, ECDsa signing)
    {
        _sealKey = sealKey;
        _signing = signing;
        byte[] spki = signing.ExportSubjectPublicKeyInfo();
        PublicKey = Convert.ToBase64String(spki);
        KeyId = KeyIdOf(spki);
    }

    public string KeyId { get; }

    public string PublicKey { get; }

    public static string KeyIdOf(byte[] subjectPublicKeyInfo) =>
        "p256-" + Convert.ToHexStringLower(SHA256.HashData(subjectPublicKeyInfo))[..16];

    public string Seal(string entryHash) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_sealKey, Encoding.ASCII.GetBytes(entryHash)));

    public bool VerifySeal(string entryHash, string seal)
    {
        if (string.IsNullOrEmpty(seal) || seal.Length != JournalEntry.HashLength)
        {
            return false;
        }

        byte[] expected = HMACSHA256.HashData(_sealKey, Encoding.ASCII.GetBytes(entryHash));
        byte[] presented;
        try
        {
            presented = Convert.FromHexString(seal);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expected, presented);
    }

    public string Sign(string closingHash) =>
        Convert.ToBase64String(_signing.SignData(Encoding.ASCII.GetBytes(closingHash), HashAlgorithmName.SHA256));

    public bool VerifyClosingSignature(string keyId, string publicKey, string closingHash, string signature)
    {
        // Une clôture re-signée avec une autre clé (jointe dans la ligne) est refusée : seule la clé du cabinet compte.
        if (!string.Equals(keyId, KeyId, StringComparison.Ordinal) || !string.Equals(publicKey, PublicKey, StringComparison.Ordinal))
        {
            return false;
        }

        return VerifyWithPublicKey(publicKey, closingHash, signature);
    }

    /// <summary>Vérification avec la seule clé publique : ce que fait un auditeur avec une archive scellée.</summary>
    public static bool VerifyWithPublicKey(string publicKey, string hash, string signature)
    {
        try
        {
            using ECDsa verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return verifier.VerifyData(Encoding.ASCII.GetBytes(hash), Convert.FromBase64String(signature), HashAlgorithmName.SHA256);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return false;
        }
    }
}
