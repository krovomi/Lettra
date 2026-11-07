using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Lettra.Infrastructure.Persistence.Security;

/// <summary>
/// Champs sensibles d'un dossier (IBAN, adresses e-mail…), chiffrés dans l'application par la DEK du dossier (ADR-0011,
/// option C du plan du mode 5) : une base copiée hors de la cellule, ou restaurée d'une sauvegarde, ne les laisse pas
/// lire sans la clé, et détruire la DEK les rend illisibles pour de bon.
/// <para>
/// Le modèle EF marque la valeur à écrire (<see cref="PendingMarker"/>) ; l'intercepteur du contexte
/// (<see cref="SensitiveFieldInterceptor"/>) la chiffre juste avant l'envoi au serveur et déchiffre à la lecture. Une valeur
/// écrite avant le chiffrement, sans préfixe, se relit telle quelle : les données d'avant la mise à jour restent lisibles.
/// </para>
/// </summary>
public static class SensitiveField
{
    /// <summary>Préfixe d'une valeur chiffrée en base.</summary>
    public const string EncryptedPrefix = "lettra:enc:v1:";

    /// <summary>Marque posée par le modèle sur une valeur à chiffrer ; jamais stockée (retirée à défaut de DEK).</summary>
    internal const string PendingMarker = "\u0001lettra\u0001";

    internal const string Annotation = "Lettra:Sensitive";

    /// <summary>
    /// Longueur maximale en base d'un champ de <paramref name="maxCharacters"/> caractères une fois chiffré : nonce, tag et
    /// texte (jusqu'à 3 octets UTF-8 par caractère) en base 64, préfixe compris.
    /// </summary>
    public static int StoredLength(int maxCharacters) =>
        EncryptedPrefix.Length + (4 * (((SensitiveFieldCipher.Overhead + (3 * maxCharacters)) + 2) / 3));

    /// <summary>
    /// Marque une propriété comme sensible : sa conversion existante (objet valeur → texte) est complétée par la marque à
    /// chiffrer, et la colonne élargie pour le texte chiffré.
    /// </summary>
    public static PropertyBuilder<TProperty> IsSensitive<TProperty>(this PropertyBuilder<TProperty> builder, int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ValueConverter marker = new MarkerConverter();
        ValueConverter converter = builder.Metadata.GetValueConverter() is { } existing ? existing.ComposeWith(marker) : marker;
        builder.Metadata.SetValueConverter(converter);
        builder.HasMaxLength(StoredLength(maxCharacters));
        builder.HasAnnotation(Annotation, true);
        return builder;
    }

    /// <summary>Le texte en clair d'une valeur marquée (la marque retirée) ; une valeur non marquée est rendue telle quelle.</summary>
    internal static string Unmark(string value) =>
        value.StartsWith(PendingMarker, StringComparison.Ordinal) ? value[PendingMarker.Length..] : value;

    private sealed class MarkerConverter() : ValueConverter<string, string>(
        v => PendingMarker + v,
        v => Unmark(v));
}

/// <summary>
/// Chiffrement déterministe d'un champ (construction à IV synthétique) : le nonce AES-GCM est le HMAC-SHA256 du texte,
/// tronqué. Deux valeurs égales donnent le même chiffré, ce qui garde les égalités des requêtes (aucun tri ni recherche
/// partielle sur ces champs) ; tout le reste est caché. Clés dérivées (HKDF) de la DEK du dossier, jamais la DEK elle-même.
/// </summary>
public sealed class SensitiveFieldCipher
{
    internal const int Overhead = NonceSize + TagSize;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private static readonly byte[] AssociatedData = Encoding.ASCII.GetBytes("lettra/champ/v1");

    private readonly byte[] _encryptionKey;
    private readonly byte[] _ivKey;

    /// <param name="dek">DEK du dossier (32 octets) ; elle n'est pas gardée, seules les clés dérivées le sont.</param>
    public SensitiveFieldCipher(ReadOnlySpan<byte> dek)
    {
        if (dek.Length != KeySize)
        {
            throw new CryptographicException("DEK du dossier invalide.");
        }

        _encryptionKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, dek.ToArray(), KeySize, salt: [], Encoding.ASCII.GetBytes("lettra/champs/chiffrement/v1"));
        _ivKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, dek.ToArray(), KeySize, salt: [], Encoding.ASCII.GetBytes("lettra/champs/iv/v1"));
    }

    public string Encrypt(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        byte[] clear = Encoding.UTF8.GetBytes(plaintext);
        byte[] sealedValue = new byte[Overhead + clear.Length];
        Span<byte> nonce = sealedValue.AsSpan(0, NonceSize);
        HMACSHA256.HashData(_ivKey, clear).AsSpan(0, NonceSize).CopyTo(nonce);
        using AesGcm aes = new(_encryptionKey, TagSize);
        aes.Encrypt(nonce, clear, sealedValue.AsSpan(Overhead), sealedValue.AsSpan(NonceSize, TagSize), AssociatedData);
        CryptographicOperations.ZeroMemory(clear);
        return SensitiveField.EncryptedPrefix + Convert.ToBase64String(sealedValue);
    }

    /// <summary>Déchiffre une valeur préfixée ; échoue si elle a été altérée ou chiffrée par une autre DEK.</summary>
    public string Decrypt(string stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        byte[] sealedValue = Convert.FromBase64String(stored[SensitiveField.EncryptedPrefix.Length..]);
        if (sealedValue.Length < Overhead)
        {
            throw new CryptographicException("Champ chiffré tronqué.");
        }

        byte[] clear = new byte[sealedValue.Length - Overhead];
        using AesGcm aes = new(_encryptionKey, TagSize);
        aes.Decrypt(sealedValue.AsSpan(0, NonceSize), sealedValue.AsSpan(Overhead), sealedValue.AsSpan(NonceSize, TagSize), clear, AssociatedData);
        try
        {
            return Encoding.UTF8.GetString(clear);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
        }
    }
}

/// <summary>
/// DEK d'un dossier serveur, pour chiffrer ses champs sensibles. Sur un poste (modes 1 et 4), la base entière est déjà
/// chiffrée par la DEK (SQLite chiffré) : aucune clé de champ n'est donnée et les champs restent en clair dans le fichier.
/// </summary>
public interface IDossierFieldKeys
{
    /// <summary>
    /// La DEK du dossier (copie, à effacer après usage), ou <c>null</c> si ses champs ne sont pas chiffrés (base déclarée
    /// sans DEK, poste). Échoue si la DEK devrait exister mais ne s'ouvre pas (clé détruite : dossier illisible).
    /// </summary>
    ValueTask<byte[]?> FindAsync(Application.Abstractions.Tenancy.TenantDescriptor tenant, CancellationToken cancellationToken);
}

/// <summary>Aucune clé de champ : modes du poste et hôtes sans catalogue.</summary>
internal sealed class NoDossierFieldKeys : IDossierFieldKeys
{
    public ValueTask<byte[]?> FindAsync(Application.Abstractions.Tenancy.TenantDescriptor tenant, CancellationToken cancellationToken) =>
        ValueTask.FromResult<byte[]?>(null);
}
