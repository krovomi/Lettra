using System.Security.Cryptography;
using Lettra.Domain.SharedKernel;

namespace Lettra.Infrastructure.Persistence.Security;

/// <summary>
/// Fournit la clé maîtresse (KEK) d'un cabinet (ADR-0011). Selon le mode : coffre de l'OS (poste, via MAUI
/// SecureStorage), coffre de la cellule injecté en configuration (serveur, SaaS), puis KMS / HSM ou clé
/// détenue par le cabinet (BYOK) sans changer le reste.
/// </summary>
public interface IKekStore
{
    /// <summary>Renvoie la KEK du cabinet, en la créant si le magasin le permet (poste isolé).</summary>
    ValueTask<byte[]> GetOrCreateAsync(OrganisationId organisation, CancellationToken cancellationToken);

    /// <summary>Renvoie la KEK du cabinet, ou <c>null</c> si elle est absente (dossiers illisibles).</summary>
    ValueTask<byte[]?> FindAsync(OrganisationId organisation, CancellationToken cancellationToken);
}

/// <summary>Hiérarchie de clés de l'ADR-0011 : la KEK du cabinet chiffre une clé (DEK) par dossier.</summary>
public interface IDossierKeyring
{
    /// <summary>Crée la DEK d'un nouveau dossier et la renvoie enveloppée par la KEK du cabinet (à stocker).</summary>
    ValueTask<byte[]> CreateWrappedDekAsync(OrganisationId organisation, DossierId dossier, CancellationToken cancellationToken);

    /// <summary>Déchiffre la DEK d'un dossier. Échoue si la KEK est absente ou si l'enveloppe a été altérée ou effacée.</summary>
    ValueTask<byte[]> UnwrapDekAsync(OrganisationId organisation, DossierId dossier, byte[] wrappedDek, CancellationToken cancellationToken);
}

/// <summary>
/// DEK de 256 bits par dossier, enveloppée en AES-256-GCM par la KEK du cabinet. Les identifiants du cabinet et
/// du dossier sont liés à l'enveloppe (données associées) : la DEK d'un dossier ne peut pas être recollée sur un
/// autre. Effacer l'enveloppe rend le dossier illisible (effacement par destruction de clé).
/// </summary>
internal sealed class AesGcmDossierKeyring(IKekStore kekStore) : IDossierKeyring
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    public async ValueTask<byte[]> CreateWrappedDekAsync(OrganisationId organisation, DossierId dossier, CancellationToken cancellationToken)
    {
        byte[] kek = await kekStore.GetOrCreateAsync(organisation, cancellationToken).ConfigureAwait(false);
        byte[] dek = RandomNumberGenerator.GetBytes(KeySize);
        try
        {
            byte[] wrapped = new byte[NonceSize + TagSize + KeySize];
            RandomNumberGenerator.Fill(wrapped.AsSpan(0, NonceSize));
            using AesGcm aes = new(kek, TagSize);
            aes.Encrypt(
                wrapped.AsSpan(0, NonceSize),
                dek,
                wrapped.AsSpan(NonceSize + TagSize),
                wrapped.AsSpan(NonceSize, TagSize),
                AssociatedData(organisation, dossier));
            return wrapped;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    public async ValueTask<byte[]> UnwrapDekAsync(OrganisationId organisation, DossierId dossier, byte[] wrappedDek, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wrappedDek);
        byte[] kek = await kekStore.FindAsync(organisation, cancellationToken).ConfigureAwait(false)
            ?? throw new CryptographicException("Clé du cabinet indisponible : dossier illisible.");
        if (wrappedDek.Length != NonceSize + TagSize + KeySize)
        {
            throw new CryptographicException("Enveloppe de clé absente ou invalide : dossier illisible.");
        }

        byte[] dek = new byte[KeySize];
        using AesGcm aes = new(kek, TagSize);
        aes.Decrypt(
            wrappedDek.AsSpan(0, NonceSize),
            wrappedDek.AsSpan(NonceSize + TagSize),
            wrappedDek.AsSpan(NonceSize, TagSize),
            dek,
            AssociatedData(organisation, dossier));
        return dek;
    }

    private static byte[] AssociatedData(OrganisationId organisation, DossierId dossier) =>
        [.. organisation.Value.ToByteArray(), .. dossier.Value.ToByteArray()];
}

/// <summary>KEK en mémoire, pour les tests uniquement.</summary>
public sealed class InMemoryKekStore : IKekStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<OrganisationId, byte[]> _keys = new();

    public ValueTask<byte[]> GetOrCreateAsync(OrganisationId organisation, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_keys.GetOrAdd(organisation, _ => RandomNumberGenerator.GetBytes(32)));

    public ValueTask<byte[]?> FindAsync(OrganisationId organisation, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_keys.TryGetValue(organisation, out byte[]? key) ? key : null);

    public void Forget(OrganisationId organisation) => _keys.TryRemove(organisation, out _);
}
