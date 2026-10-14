using System.Security.Cryptography;
using Lettra.Application.Abstractions.Security;

namespace Lettra.Infrastructure.Security;

/// Fournit la clé maîtresse (KEK) d'un cabinet. En production : KMS / HSM (SaaS), coffre de l'OS (poste),
/// ou clé détenue par le cabinet (BYOK / HYOK). Ici : un fichier par cabinet, pour le POC uniquement.
public interface IKekStore
{
    ValueTask<byte[]> GetOrCreateAsync(Guid organisationId, CancellationToken ct = default);
    ValueTask<byte[]?> FindAsync(Guid organisationId, CancellationToken ct = default);
}

public sealed class FileKekStore(string directory) : IKekStore
{
    public async ValueTask<byte[]> GetOrCreateAsync(Guid organisationId, CancellationToken ct = default)
    {
        if (await FindAsync(organisationId, ct) is { } existing)
            return existing;

        Directory.CreateDirectory(directory);
        var kek = RandomNumberGenerator.GetBytes(32);
        var path = PathFor(organisationId);
        try
        {
            await using var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            await f.WriteAsync(kek, ct);
            return kek;
        }
        catch (IOException) when (File.Exists(path))
        {
            return await File.ReadAllBytesAsync(path, ct);   // créée en parallèle par un autre fil
        }
    }

    public async ValueTask<byte[]?> FindAsync(Guid organisationId, CancellationToken ct = default)
    {
        var path = PathFor(organisationId);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null;
    }

    private string PathFor(Guid organisationId) => Path.Combine(directory, $"kek-{organisationId:N}.bin");
}

/// DEK de 256 bits par dossier, enveloppée en AES-256-GCM par la KEK du cabinet.
/// Les identifiants du cabinet et du dossier sont liés à l'enveloppe (données associées) :
/// la DEK d'un dossier ne peut pas être recollée sur un autre.
public sealed class AesGcmDossierKeyring(IKekStore kekStore) : IDossierKeyring
{
    private const int NonceSize = 12, TagSize = 16, KeySize = 32;

    public async ValueTask<byte[]> CreateWrappedDekAsync(Guid organisationId, Guid dossierId, CancellationToken ct = default)
    {
        var kek = await kekStore.GetOrCreateAsync(organisationId, ct);
        var dek = RandomNumberGenerator.GetBytes(KeySize);
        var wrapped = new byte[NonceSize + TagSize + KeySize];
        var nonce = wrapped.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(kek, TagSize);
        aes.Encrypt(nonce, dek, wrapped.AsSpan(NonceSize + TagSize), wrapped.AsSpan(NonceSize, TagSize), Aad(organisationId, dossierId));
        CryptographicOperations.ZeroMemory(dek);
        return wrapped;
    }

    public async ValueTask<byte[]> UnwrapDekAsync(Guid organisationId, Guid dossierId, byte[] wrappedDek, CancellationToken ct = default)
    {
        var kek = await kekStore.FindAsync(organisationId, ct)
                  ?? throw new CryptographicException($"Clé du cabinet {organisationId} indisponible : dossier illisible.");
        if (wrappedDek.Length != NonceSize + TagSize + KeySize)
            throw new CryptographicException("Enveloppe de clé invalide.");
        var dek = new byte[KeySize];
        using var aes = new AesGcm(kek, TagSize);
        aes.Decrypt(wrappedDek.AsSpan(0, NonceSize), wrappedDek.AsSpan(NonceSize + TagSize), wrappedDek.AsSpan(NonceSize, TagSize), dek, Aad(organisationId, dossierId));
        return dek;
    }

    private static byte[] Aad(Guid organisationId, Guid dossierId) =>
        [.. organisationId.ToByteArray(), .. dossierId.ToByteArray()];
}
