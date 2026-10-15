using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Integrity;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Persistence.Security;

/// <summary>
/// Registre des clés de la chaîne d'un dossier (ADR-0011, annexe F §6). Chaque version porte une clé de sceau
/// (HMAC-SHA256) et une clé de signature (ECDSA P-256), tirées au hasard et enveloppées ensemble en AES-256-GCM par la
/// clé maîtresse (KEK) du cabinet. Les données associées lient l'enveloppe au cabinet, au dossier, au numéro de
/// version et à l'identifiant : une ligne recopiée d'un autre dossier, renumérotée ou fabriquée sans la KEK ne s'ouvre
/// pas. Une rotation ajoute une version ; les anciennes restent, en vérification seule, toute la durée légale.
/// <para>
/// Les dossiers journalisés avant le versionnage avaient des clés dérivées (HKDF) de la KEK : à la première écriture,
/// elles deviennent la version 1, enveloppée comme les autres. Un dossier neuf commence directement par des clés tirées
/// au hasard, propres au dossier.
/// </para>
/// </summary>
internal sealed class EnvelopedIntegrityKeyring(TenantDbSession session, ITenantContext tenant, IKekStore kekStore, TimeProvider clock)
    : IIntegrityKeyring
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int SecretSize = 64;
    private static readonly byte[] Purpose = Encoding.ASCII.GetBytes("lettra/integrite/cle/v1");

    private readonly Dictionary<string, IntegrityKeyMaterial> _opened = new(StringComparer.Ordinal);

    public async Task<Result<IIntegrityKeyVerifier>> VerifierAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        byte[]? kek = await kekStore.FindAsync(tenant.Current.OrganisationId, cancellationToken).ConfigureAwait(false);
        if (kek is null)
        {
            return IntegrityErrors.KeyUnavailable;
        }

        List<StoredIntegrityKey> rows = await db.IntegrityKeys.AsNoTracking().OrderBy(k => k.Version).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            // Journal d'avant le versionnage (ou vide) : sa seule clé est la clé dérivée.
            IntegrityKeyMaterial legacy = LegacyIntegrityKeys.Derive(kek, tenant.Current.OrganisationId);
            return new KeyRegistryVerifier([legacy], legacy.KeyId, legacy.KeyId);
        }

        // Une enveloppe qui ne s'ouvre pas (ligne fabriquée, altérée, recopiée) laisse sa version inconnue : les entrées
        // qu'elle aurait scellées sont signalées par le contrôle, au lieu de le faire échouer.
        List<IntegrityKeyMaterial> opened = [.. rows.Select(r => TryOpen(kek, r)).OfType<IntegrityKeyMaterial>()];
        return new KeyRegistryVerifier(opened, rows[0].KeyId, rows[^1].KeyId);
    }

    public async Task<IReadOnlyList<IntegrityKeyVersion>> ListAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<StoredIntegrityKey> rows = await db.IntegrityKeys.AsNoTracking().OrderBy(k => k.Version).ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.Select((r, i) => new IntegrityKeyVersion(r.Version, r.KeyId, r.PublicKey, r.CreatedAt, i + 1 < rows.Count ? rows[i + 1].CreatedAt : null))];
    }

    /// <summary>
    /// La version en vigueur, pour sceller et signer. À appeler sous le verrou de la tête de chaîne, dans la
    /// transaction : la version 1 est créée au premier usage et enregistrée avec l'entrée qui l'utilise.
    /// </summary>
    internal async Task<Result<IntegrityKeyMaterial>> CurrentAsync(LettraDbContext db, CancellationToken cancellationToken)
    {
        byte[]? kek = await kekStore.FindAsync(tenant.Current.OrganisationId, cancellationToken).ConfigureAwait(false);
        if (kek is null)
        {
            return IntegrityErrors.KeyUnavailable;
        }

        StoredIntegrityKey? current = await LatestAsync(db, cancellationToken).ConfigureAwait(false);
        if (current is not null)
        {
            return TryOpen(kek, current) is { } material ? material : IntegrityErrors.KeyUnavailable;
        }

        bool hasHistory = await db.IntegrityJournal.AnyAsync(cancellationToken).ConfigureAwait(false)
            || await db.IntegrityClosings.AnyAsync(cancellationToken).ConfigureAwait(false);
        IntegrityKeyMaterial first = hasHistory ? LegacyIntegrityKeys.Derive(kek, tenant.Current.OrganisationId) : IntegrityKeyMaterial.Generate();
        db.IntegrityKeys.Add(Wrap(kek, 1, first));
        _opened[first.KeyId] = first;
        return first;
    }

    /// <summary>Prépare la version suivante (non ajoutée) : l'appelant l'annonce dans la chaîne, puis l'ajoute.</summary>
    internal async Task<Result<(IntegrityKeyMaterial Keys, StoredIntegrityKey Row)>> PrepareNextAsync(
        LettraDbContext db, CancellationToken cancellationToken)
    {
        byte[]? kek = await kekStore.FindAsync(tenant.Current.OrganisationId, cancellationToken).ConfigureAwait(false);
        if (kek is null)
        {
            return IntegrityErrors.KeyUnavailable;
        }

        StoredIntegrityKey? current = await LatestAsync(db, cancellationToken).ConfigureAwait(false);
        IntegrityKeyMaterial next = IntegrityKeyMaterial.Generate();
        _opened[next.KeyId] = next;
        return (next, Wrap(kek, (current?.Version ?? 0) + 1, next));
    }

    /// <summary>Une version précise, retirée ou non (archive d'une clôture ancienne).</summary>
    internal async Task<Result<IntegrityKeyMaterial>> FindAsync(LettraDbContext db, string keyId, CancellationToken cancellationToken)
    {
        byte[]? kek = await kekStore.FindAsync(tenant.Current.OrganisationId, cancellationToken).ConfigureAwait(false);
        if (kek is null)
        {
            return IntegrityErrors.KeyUnavailable;
        }

        StoredIntegrityKey? row = await db.IntegrityKeys.AsNoTracking().SingleOrDefaultAsync(k => k.KeyId == keyId, cancellationToken).ConfigureAwait(false);
        IntegrityKeyMaterial? material = row is not null
            ? TryOpen(kek, row)
            : !await db.IntegrityKeys.AnyAsync(cancellationToken).ConfigureAwait(false)
                ? LegacyIntegrityKeys.Derive(kek, tenant.Current.OrganisationId)
                : null;
        return material is not null && material.KeyId == keyId ? material : IntegrityErrors.KeyUnavailable;
    }

    /// <summary>Dernière version : celle ajoutée dans cette transaction si elle existe, sinon la dernière en base.</summary>
    private static async Task<StoredIntegrityKey?> LatestAsync(LettraDbContext db, CancellationToken cancellationToken)
    {
        StoredIntegrityKey? local = db.IntegrityKeys.Local.MaxBy(k => k.Version);
        StoredIntegrityKey? stored = await db.IntegrityKeys.AsNoTracking()
            .OrderByDescending(k => k.Version).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return local is not null && (stored is null || local.Version >= stored.Version) ? local : stored;
    }

    private StoredIntegrityKey Wrap(byte[] kek, int version, IntegrityKeyMaterial keys)
    {
        byte[] secret = keys.ExportSecret();
        try
        {
            byte[] wrapped = new byte[NonceSize + TagSize + SecretSize];
            RandomNumberGenerator.Fill(wrapped.AsSpan(0, NonceSize));
            using AesGcm aes = new(kek, TagSize);
            aes.Encrypt(
                wrapped.AsSpan(0, NonceSize),
                secret,
                wrapped.AsSpan(NonceSize + TagSize),
                wrapped.AsSpan(NonceSize, TagSize),
                AssociatedData(version, keys.KeyId));
            return new StoredIntegrityKey(version, keys.KeyId, keys.PublicKey, wrapped, clock.GetUtcNow());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private IntegrityKeyMaterial? TryOpen(byte[] kek, StoredIntegrityKey row)
    {
        if (_opened.TryGetValue(row.KeyId, out IntegrityKeyMaterial? cached) && cached.PublicKey == row.PublicKey)
        {
            return cached;
        }

        if (row.WrappedSecret is not { Length: NonceSize + TagSize + SecretSize })
        {
            return null;
        }

        byte[] secret = new byte[SecretSize];
        try
        {
            using AesGcm aes = new(kek, TagSize);
            aes.Decrypt(
                row.WrappedSecret.AsSpan(0, NonceSize),
                row.WrappedSecret.AsSpan(NonceSize + TagSize),
                row.WrappedSecret.AsSpan(NonceSize, TagSize),
                secret,
                AssociatedData(row.Version, row.KeyId));
            IntegrityKeyMaterial material = IntegrityKeyMaterial.FromSecret(secret);

            // La clé publique du registre doit être celle de la clé privée enveloppée, pas une valeur posée à côté.
            if (material.KeyId != row.KeyId || material.PublicKey != row.PublicKey)
            {
                return null;
            }

            _opened[row.KeyId] = material;
            return material;
        }
        catch (CryptographicException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private byte[] AssociatedData(int version, string keyId)
    {
        byte[] versionBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(versionBytes, version);
        return [.. Purpose, .. tenant.Current.OrganisationId.Value.ToByteArray(), .. tenant.Current.DossierId.Value.ToByteArray(), .. versionBytes, .. Encoding.ASCII.GetBytes(keyId)];
    }
}

/// <summary>Toutes les versions ouvertes du registre, en vérification seule.</summary>
internal sealed class KeyRegistryVerifier(IReadOnlyList<IntegrityKeyMaterial> keys, string initialKeyId, string currentKeyId) : IIntegrityKeyVerifier
{
    private readonly Dictionary<string, IntegrityKeyMaterial> _keys = keys.ToDictionary(k => k.KeyId, StringComparer.Ordinal);

    public string? InitialKeyId { get; } = initialKeyId;

    public string? CurrentKeyId { get; } = currentKeyId;

    public string? PublicKeyOf(string keyId) => _keys.GetValueOrDefault(keyId)?.PublicKey;

    public bool VerifySeal(string keyId, string entryHash, string seal) =>
        _keys.TryGetValue(keyId, out IntegrityKeyMaterial? key) && key.VerifySeal(entryHash, seal);

    public bool VerifyClosingSignature(string keyId, string publicKey, string closingHash, string signature) =>
        // Une clôture re-signée avec une clé étrangère (jointe dans la ligne) est refusée : seules comptent les versions
        // du registre, avec la clé publique de leur clé privée.
        _keys.TryGetValue(keyId, out IntegrityKeyMaterial? key)
        && string.Equals(publicKey, key.PublicKey, StringComparison.Ordinal)
        && IntegrityKeyMaterial.VerifyWithPublicKey(publicKey, closingHash, signature);
}

/// <summary>Les clés d'avant le versionnage : dérivées (HKDF-SHA256) de la KEK, communes aux dossiers du cabinet.</summary>
internal static class LegacyIntegrityKeys
{
    private const string SealInfo = "lettra/integrite/sceau/v1";
    private const string SigningInfo = "lettra/integrite/signature/v1";

    public static IntegrityKeyMaterial Derive(byte[] kek, OrganisationId cabinet)
    {
        byte[] salt = cabinet.Value.ToByteArray();
        byte[] sealKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, kek, 32, salt, Encoding.ASCII.GetBytes(SealInfo));
        for (int counter = 0; ; counter++)
        {
            byte[] scalar = HKDF.DeriveKey(HashAlgorithmName.SHA256, kek, 32, salt, Encoding.ASCII.GetBytes($"{SigningInfo}/{counter}"));
            try
            {
                return new IntegrityKeyMaterial(sealKey, ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = scalar }));
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

/// <summary>Une version de clé ouverte : sceau HMAC-SHA256 des entrées et signature ECDSA P-256 des clôtures.</summary>
internal sealed class IntegrityKeyMaterial
{
    private const int ScalarSize = 32;
    private readonly byte[] _sealKey;
    private readonly ECDsa _signing;

    public IntegrityKeyMaterial(byte[] sealKey, ECDsa signing)
    {
        _sealKey = sealKey;
        _signing = signing;
        byte[] spki = signing.ExportSubjectPublicKeyInfo();
        PublicKey = Convert.ToBase64String(spki);
        KeyId = KeyIdOf(spki);
    }

    /// <summary><c>p256-</c> et les 16 premiers caractères hexadécimaux du SHA-256 de la clé publique.</summary>
    public string KeyId { get; }

    /// <summary>Clé publique de signature (SubjectPublicKeyInfo, base 64).</summary>
    public string PublicKey { get; }

    public static IntegrityKeyMaterial Generate()
    {
        while (true)
        {
            using ECDsa candidate = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            byte[] scalar = candidate.ExportParameters(includePrivateParameters: true).D!;
            if (scalar.Length != ScalarSize)
            {
                CryptographicOperations.ZeroMemory(scalar);
                continue;
            }

            byte[] secret = [.. RandomNumberGenerator.GetBytes(32), .. scalar];
            try
            {
                return FromSecret(secret);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(scalar);
                CryptographicOperations.ZeroMemory(secret);
            }
        }
    }

    /// <summary>Clé de sceau (32 octets) suivie du scalaire privé P-256 (32 octets).</summary>
    public static IntegrityKeyMaterial FromSecret(ReadOnlySpan<byte> secret)
    {
        byte[] scalar = secret[32..].ToArray();
        try
        {
            return new IntegrityKeyMaterial(secret[..32].ToArray(), ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = scalar }));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(scalar);
        }
    }

    public byte[] ExportSecret()
    {
        byte[] scalar = _signing.ExportParameters(includePrivateParameters: true).D!;
        try
        {
            return [.. _sealKey, .. scalar];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(scalar);
        }
    }

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
