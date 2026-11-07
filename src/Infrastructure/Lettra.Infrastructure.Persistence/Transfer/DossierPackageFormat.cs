using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lettra.Infrastructure.Persistence.Transfer;

/// <summary>Ce que dit le paquet de son dossier : identité, schéma, lignes copiées par table.</summary>
internal sealed record DossierPackageManifest(
    int Format,
    Guid DossierId,
    string Name,
    DateTimeOffset ExportedAt,
    string Migration,
    IReadOnlyDictionary<string, int> Tables);

/// <summary>Une version de clé de la chaîne, en clair dans le paquet chiffré (64 octets : sceau puis signature, en base 64).</summary>
internal sealed record PackagedIntegrityKey(int Version, string KeyId, string PublicKey, DateTimeOffset CreatedAt, string Secret);

/// <summary>Le contenu ouvert d'un paquet : la base du dossier sur le disque, son manifeste et les clés de sa chaîne.</summary>
internal sealed record OpenedDossierPackage(string DatabaseFile, DossierPackageManifest Manifest, IReadOnlyList<PackagedIntegrityKey> Keys);

/// <summary>Paquet illisible : phrase secrète incorrecte, fichier tronqué, altéré ou étranger.</summary>
internal sealed class DossierPackageException : Exception
{
    public DossierPackageException()
    {
    }

    public DossierPackageException(string message)
        : base(message)
    {
    }

    public DossierPackageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Le format du paquet de réversibilité, version 1 (annexe F) : un en-tête en clair (format, paramètres de dérivation),
/// puis une archive zip découpée en morceaux chiffrés AES-256-GCM par une clé dérivée de la phrase secrète
/// (PBKDF2-SHA256, 600 000 itérations, sel aléatoire). Chaque morceau est lié à l'en-tête, à son rang et au drapeau
/// « dernier » : un morceau retiré, déplacé ou ajouté, ou un paquet tronqué, ne s'ouvre pas.
/// <para>
/// L'archive porte la base du dossier (SQLite 3, lisible par tout outil SQLite, champs sensibles en clair), son manifeste,
/// les clés de sa chaîne anti-fraude et un « LISEZMOI ». Rien n'est en clair hors de l'en-tête, qui ne nomme pas le dossier.
/// </para>
/// </summary>
internal static class DossierPackageFormat
{
    public const int Format = 1;

    internal const string DatabaseEntry = "dossier.db";
    internal const string ManifestEntry = "manifeste.json";
    internal const string KeysEntry = "cles-integrite.json";
    internal const string ReadMeEntry = "LISEZMOI.txt";

    private const int SaltSize = 16;
    private const int NoncePrefixSize = 4;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int DefaultChunkSize = 1 << 20;
    private const int MaxChunkSize = 16 << 20;
    private const int MaxHeaderLength = 4096;
    private const int MinIterations = 100_000;
    private const int MaxIterations = 10_000_000;
    private const long MaxDatabaseLength = 8L << 30;

    private static ReadOnlySpan<byte> Magic => "LETTRA-PAQUET-DOSSIER\n"u8;

    /// <summary>Itérations PBKDF2 des paquets écrits (recommandation OWASP pour PBKDF2-HMAC-SHA256).</summary>
    internal const int Iterations = 600_000;

    /// <summary>Range la base, le manifeste et les clés dans l'archive à chiffrer.</summary>
    public static void WriteArchive(string archiveFile, string databaseFile, DossierPackageManifest manifest, IReadOnlyList<PackagedIntegrityKey> keys)
    {
        using ZipArchive archive = ZipFile.Open(archiveFile, ZipArchiveMode.Create);
        archive.CreateEntryFromFile(databaseFile, DatabaseEntry, CompressionLevel.Optimal);
        WriteText(archive, ManifestEntry, JsonSerializer.Serialize(manifest, TransferJsonContext.Default.DossierPackageManifest));
        WriteText(archive, KeysEntry, JsonSerializer.Serialize(keys, TransferJsonContext.Default.IReadOnlyListPackagedIntegrityKey));
        WriteText(archive, ReadMeEntry, ReadMe);
    }

    /// <summary>Lit l'archive déchiffrée : seules les entrées connues sont lues, par leur nom, jamais extraites ailleurs.</summary>
    public static OpenedDossierPackage ReadArchive(string archiveFile, string workDirectory)
    {
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(archiveFile);
            DossierPackageManifest manifest = JsonSerializer.Deserialize(ReadText(archive, ManifestEntry), TransferJsonContext.Default.DossierPackageManifest)
                ?? throw new DossierPackageException("Manifeste vide.");
            IReadOnlyList<PackagedIntegrityKey> keys = JsonSerializer.Deserialize(ReadText(archive, KeysEntry), TransferJsonContext.Default.IReadOnlyListPackagedIntegrityKey)
                ?? throw new DossierPackageException("Clés absentes.");
            ZipArchiveEntry database = archive.GetEntry(DatabaseEntry) ?? throw new DossierPackageException("Base absente.");
            if (database.Length > MaxDatabaseLength)
            {
                throw new DossierPackageException("Base trop volumineuse.");
            }

            string databaseFile = Path.Combine(workDirectory, DatabaseEntry);
            database.ExtractToFile(databaseFile, overwrite: false);
            return new OpenedDossierPackage(databaseFile, manifest, keys);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or IOException)
        {
            throw new DossierPackageException("Archive du paquet illisible.", exception);
        }
    }

    /// <summary>Chiffre <paramref name="archive"/> dans <paramref name="destination"/> par la phrase secrète.</summary>
    public static async Task EncryptAsync(Stream archive, Stream destination, string passphrase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(destination);
        PackageHeader header = new(Format, "pbkdf2-sha256", Iterations, RandomNumberGenerator.GetBytes(SaltSize), RandomNumberGenerator.GetBytes(NoncePrefixSize), DefaultChunkSize);
        byte[] headerBytes = EncodeHeader(header);
        await destination.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);

        byte[] key = DeriveKey(passphrase, header);
        byte[] plain = new byte[header.ChunkSize];
        byte[] next = new byte[header.ChunkSize];
        byte[] cipher = new byte[header.ChunkSize];
        byte[] tag = new byte[TagSize];
        byte[] prefix = new byte[5];
        try
        {
            using AesGcm aes = new(key, TagSize);
            int current = await FillAsync(archive, plain, cancellationToken).ConfigureAwait(false);
            for (long index = 0; ; index++)
            {
                // Lire un morceau d'avance dit si celui-ci est le dernier ; une archive vide fait un seul morceau vide.
                int following = current == header.ChunkSize ? await FillAsync(archive, next, cancellationToken).ConfigureAwait(false) : 0;
                bool last = following == 0;
                aes.Encrypt(Nonce(header, index), plain.AsSpan(0, current), cipher.AsSpan(0, current), tag, ChunkData(headerBytes, index, last));
                prefix[0] = last ? (byte)1 : (byte)0;
                BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(1), current);
                await destination.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
                await destination.WriteAsync(cipher.AsMemory(0, current), cancellationToken).ConfigureAwait(false);
                await destination.WriteAsync(tag, cancellationToken).ConfigureAwait(false);
                if (last)
                {
                    return;
                }

                (plain, next) = (next, plain);
                current = following;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(next);
        }
    }

    /// <summary>Déchiffre le paquet dans <paramref name="archive"/> ; <see cref="DossierPackageException"/> s'il ne s'ouvre pas.</summary>
    public static async Task DecryptAsync(Stream package, Stream archive, string passphrase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(archive);
        (PackageHeader header, byte[] headerBytes) = await ReadHeaderAsync(package, cancellationToken).ConfigureAwait(false);
        byte[] key = DeriveKey(passphrase, header);
        byte[] cipher = new byte[header.ChunkSize];
        byte[] plain = new byte[header.ChunkSize];
        byte[] tag = new byte[TagSize];
        byte[] prefix = new byte[5];
        try
        {
            using AesGcm aes = new(key, TagSize);
            for (long index = 0; ; index++)
            {
                await ReadExactlyAsync(package, prefix, cancellationToken).ConfigureAwait(false);
                bool last = prefix[0] switch
                {
                    0 => false,
                    1 => true,
                    _ => throw new DossierPackageException("Morceau mal formé."),
                };
                int length = BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(1));
                if (length < 0 || length > header.ChunkSize || (!last && length != header.ChunkSize))
                {
                    throw new DossierPackageException("Morceau mal formé.");
                }

                await ReadExactlyAsync(package, cipher.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                await ReadExactlyAsync(package, tag, cancellationToken).ConfigureAwait(false);
                aes.Decrypt(Nonce(header, index), cipher.AsSpan(0, length), tag, plain.AsSpan(0, length), ChunkData(headerBytes, index, last));
                await archive.WriteAsync(plain.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                if (last)
                {
                    if (await package.ReadAsync(prefix.AsMemory(0, 1), cancellationToken).ConfigureAwait(false) != 0)
                    {
                        throw new DossierPackageException("Données après le dernier morceau.");
                    }

                    return;
                }
            }
        }
        catch (AuthenticationTagMismatchException exception)
        {
            throw new DossierPackageException("Phrase secrète incorrecte ou paquet altéré.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static byte[] EncodeHeader(PackageHeader header)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(header, TransferJsonContext.Default.PackageHeader);
        byte[] bytes = new byte[Magic.Length + 4 + json.Length];
        Magic.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(Magic.Length), json.Length);
        json.CopyTo(bytes.AsSpan(Magic.Length + 4));
        return bytes;
    }

    private static async Task<(PackageHeader Header, byte[] Bytes)> ReadHeaderAsync(Stream package, CancellationToken cancellationToken)
    {
        byte[] start = new byte[Magic.Length + 4];
        await ReadExactlyAsync(package, start, cancellationToken).ConfigureAwait(false);
        if (!start.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new DossierPackageException("Ce fichier n'est pas un paquet de dossier Lettra.");
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(start.AsSpan(Magic.Length));
        if (length is <= 0 or > MaxHeaderLength)
        {
            throw new DossierPackageException("En-tête mal formé.");
        }

        byte[] bytes = new byte[start.Length + length];
        start.CopyTo(bytes, 0);
        await ReadExactlyAsync(package, bytes.AsMemory(start.Length), cancellationToken).ConfigureAwait(false);
        PackageHeader? header;
        try
        {
            header = JsonSerializer.Deserialize(bytes.AsSpan(start.Length), TransferJsonContext.Default.PackageHeader);
        }
        catch (JsonException exception)
        {
            throw new DossierPackageException("En-tête mal formé.", exception);
        }

        return header is { Format: Format, Kdf: "pbkdf2-sha256", Salt.Length: >= SaltSize, NoncePrefix.Length: NoncePrefixSize }
            && header.Iterations is >= MinIterations and <= MaxIterations
            && header.ChunkSize is > 0 and <= MaxChunkSize
            ? (header, bytes)
            : throw new DossierPackageException("En-tête d'un format inconnu.");
    }

    private static byte[] DeriveKey(string passphrase, PackageHeader header) =>
        Rfc2898DeriveBytes.Pbkdf2(passphrase, header.Salt, header.Iterations, HashAlgorithmName.SHA256, KeySize);

    private static byte[] Nonce(PackageHeader header, long index)
    {
        byte[] nonce = new byte[NonceSize];
        header.NoncePrefix.CopyTo(nonce, 0);
        BinaryPrimitives.WriteInt64BigEndian(nonce.AsSpan(NoncePrefixSize), index);
        return nonce;
    }

    private static byte[] ChunkData(byte[] headerBytes, long index, bool last)
    {
        byte[] data = new byte[headerBytes.Length + 9];
        headerBytes.CopyTo(data, 0);
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(headerBytes.Length), index);
        data[^1] = last ? (byte)1 : (byte)0;
        return data;
    }

    private static async Task<int> FillAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int filled = 0;
        while (filled < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(filled), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        return filled;
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        try
        {
            await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException exception)
        {
            throw new DossierPackageException("Paquet tronqué.", exception);
        }
    }

    private static void WriteText(ZipArchive archive, string name, string text)
    {
        using Stream stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
        stream.Write(Encoding.UTF8.GetBytes(text));
    }

    private static string ReadText(ZipArchive archive, string name)
    {
        ZipArchiveEntry entry = archive.GetEntry(name) ?? throw new DossierPackageException($"Entrée {name} absente.");
        if (entry.Length > MaxHeaderLength * 256)
        {
            throw new DossierPackageException($"Entrée {name} trop volumineuse.");
        }

        using StreamReader reader = new(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private const string ReadMe = """
        Paquet de réversibilité d'un dossier Lettra
        ===========================================

        Ce paquet contient un dossier complet, tel qu'il était au moment de l'export :

        - dossier.db : la base du dossier, au format SQLite 3. Elle s'ouvre avec n'importe quel outil SQLite
          (par exemple « DB Browser for SQLite »). Les champs sensibles (IBAN, adresses électroniques) y sont en clair.
        - manifeste.json : l'identifiant et le nom du dossier, la date de l'export, la version du schéma et le
          nombre de lignes de chaque table.
        - cles-integrite.json : les clés qui scellent et signent la chaîne anti-fraude du dossier. Elles sont
          secrètes : quiconque les détient peut fabriquer des sceaux pour ce dossier.

        Lettra importe ce paquet tel quel (écran « Dossiers du cabinet », « Importer un dossier ») : le dossier garde
        son identifiant, ses pièces et sa chaîne, dont les clés sont alors protégées par la clé du cabinet d'arrivée.

        Gardez ce fichier et sa phrase secrète séparément. Une fois le dossier importé et vérifié, effacez les copies
        déchiffrées de ce contenu.
        """;
}

/// <summary>En-tête en clair du paquet.</summary>
internal sealed record PackageHeader(int Format, string Kdf, int Iterations, byte[] Salt, byte[] NoncePrefix, int ChunkSize);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(PackageHeader))]
[JsonSerializable(typeof(DossierPackageManifest))]
[JsonSerializable(typeof(IReadOnlyList<PackagedIntegrityKey>))]
internal sealed partial class TransferJsonContext : JsonSerializerContext;
