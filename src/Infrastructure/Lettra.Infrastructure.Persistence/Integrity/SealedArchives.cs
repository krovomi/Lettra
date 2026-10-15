using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Persistence.Integrity;

/// <summary>
/// Archive scellée d'une clôture : un ZIP autoportant (journal de la période, clôture, manifeste des empreintes,
/// signature du manifeste et clé publique). Elle se vérifie hors de Lettra : recalculer la chaîne, les empreintes
/// des fichiers, puis la signature avec la clé publique, dont l'identifiant figure au registre des clés du dossier.
/// Le manifeste est signé avec la version de clé qui a signé la clôture, même retirée depuis : l'archive ne porte
/// qu'une clé publique, et une rotation ne change pas l'archive d'une clôture ancienne.
/// Toujours régénérée à l'identique depuis la base : rien à stocker, et une base altérée produit une archive qui
/// ne se vérifie plus.
/// </summary>
internal sealed class EfSealedArchiveBuilder(TenantDbSession session, ITenantContext tenant, EnvelopedIntegrityKeyring keyring) : ISealedArchiveBuilder
{
    public async Task<Result<SealedArchive>> BuildAsync(int closingNumber, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        PeriodClosing? closing = await db.IntegrityClosings.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Number == closingNumber, cancellationToken).ConfigureAwait(false);
        if (closing is null)
        {
            return IntegrityErrors.ClosingNotFound;
        }

        Result<IntegrityKeyMaterial> keys = await keyring.FindAsync(db, closing.KeyId, cancellationToken).ConfigureAwait(false);
        if (keys.IsFailure)
        {
            return Result<SealedArchive>.Failure(keys.Errors);
        }

        List<JournalEntry> entries = await db.IntegrityJournal.AsNoTracking()
            .Where(e => e.Sequence >= closing.FirstSequence && e.Sequence <= closing.LastSequence)
            .OrderBy(e => e.Sequence)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        string name = $"lettra-{tenant.Current.DossierId.Value:N}-{PeriodClosing.FormatNumber(closing.Number)}.zip";
        return new SealedArchive(name, SealedArchiveFormat.Write(tenant.Current.DossierId.Value, closing, entries, keys.Value));
    }
}

/// <summary>Format de l'archive scellée, version 1. Écrit et relu ici ; le test d'archive le vérifie de bout en bout.</summary>
public static class SealedArchiveFormat
{
    public const string Version = "lettra-archive-scellee-v1";
    public const string JournalFile = "journal.jsonl";
    public const string ClosingFile = "cloture.json";
    public const string ManifestFile = "manifeste.json";
    public const string SignatureFile = "manifeste.sig";
    public const string PublicKeyFile = "cle-publique.pem";
    public const string ReadMeFile = "LISEZMOI.txt";

    private static readonly DateTimeOffset FixedTimestamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    internal static byte[] Write(Guid dossier, PeriodClosing closing, IReadOnlyList<JournalEntry> entries, IntegrityKeyMaterial keys)
    {
        StringBuilder journal = new();
        foreach (JournalEntry entry in entries)
        {
            journal.Append(JsonSerializer.Serialize(ArchivedEntry.From(entry), Json)).Append('\n');
        }

        Dictionary<string, byte[]> files = new(StringComparer.Ordinal)
        {
            [JournalFile] = Encoding.UTF8.GetBytes(journal.ToString()),
            [ClosingFile] = JsonSerializer.SerializeToUtf8Bytes(ArchivedClosing.From(closing), Json),
            [PublicKeyFile] = Encoding.ASCII.GetBytes(Pem(closing.PublicKey)),
            [ReadMeFile] = Encoding.UTF8.GetBytes(ReadMe),
        };

        Manifest manifest = new(
            Version,
            dossier,
            closing.Number,
            closing.Hash,
            closing.KeyId,
            [.. files.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => new ManifestFileEntry(f.Key, Sha256(f.Value)))]);
        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
        files[ManifestFile] = manifestBytes;
        files[SignatureFile] = Encoding.ASCII.GetBytes(keys.Sign(Sha256(manifestBytes)));

        using MemoryStream output = new();
        using (ZipArchive zip = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string fileName, byte[] content) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                ZipArchiveEntry zipEntry = zip.CreateEntry(fileName, CompressionLevel.Optimal);
                zipEntry.LastWriteTime = FixedTimestamp;
                using Stream stream = zipEntry.Open();
                stream.Write(content);
            }
        }

        return output.ToArray();
    }

    /// <summary>
    /// Vérifie une archive avec la seule clé publique qu'elle contient, et l'identifiant de clé attendu (celui que le
    /// registre des clés du dossier donne pour cette clôture) : signature du manifeste, empreinte de chaque fichier, chaîne des entrées et empreinte de la clôture.
    /// </summary>
    public static bool Verify(byte[] archive, string expectedKeyId, out string? failure)
    {
        ArgumentNullException.ThrowIfNull(archive);
        try
        {
            using ZipArchive zip = new(new MemoryStream(archive), ZipArchiveMode.Read);
            Dictionary<string, byte[]> files = zip.Entries.ToDictionary(e => e.FullName, Read, StringComparer.Ordinal);
            Manifest manifest = JsonSerializer.Deserialize<Manifest>(files[ManifestFile], Json)!;
            ArchivedClosing closing = JsonSerializer.Deserialize<ArchivedClosing>(files[ClosingFile], Json)!;

            if (manifest.Format != Version || manifest.KeyId != expectedKeyId || closing.KeyId != expectedKeyId
                || IntegrityKeyMaterial.KeyIdOf(Convert.FromBase64String(closing.PublicKey)) != expectedKeyId)
            {
                failure = "clé";
                return false;
            }

            string signature = Encoding.ASCII.GetString(files[SignatureFile]);
            if (!IntegrityKeyMaterial.VerifyWithPublicKey(closing.PublicKey, Sha256(files[ManifestFile]), signature))
            {
                failure = "signature";
                return false;
            }

            foreach (ManifestFileEntry file in manifest.Files)
            {
                if (!files.TryGetValue(file.Name, out byte[]? content) || Sha256(content) != file.Sha256)
                {
                    failure = file.Name;
                    return false;
                }
            }

            PeriodClosing restored = closing.ToClosing();
            Domain.SharedKernel.DossierId dossier = new(manifest.Dossier);
            if (restored.ComputeHash(dossier) != manifest.ClosingHash || closing.Hash != manifest.ClosingHash
                || !IntegrityKeyMaterial.VerifyWithPublicKey(closing.PublicKey, closing.Hash, closing.Signature))
            {
                failure = ClosingFile;
                return false;
            }

            string previous = closing.FirstSequence == 1 ? JournalEntry.GenesisHash(dossier) : string.Empty;
            long expected = closing.FirstSequence;
            string? lastHash = null;
            foreach (string line in Encoding.UTF8.GetString(files[JournalFile]).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                JournalEntry entry = JsonSerializer.Deserialize<ArchivedEntry>(line, Json)!.ToEntry();
                bool chained = previous.Length == 0 || entry.PreviousHash == previous;
                if (entry.Sequence != expected || entry.ComputeHash() != entry.Hash || !chained)
                {
                    failure = string.Create(CultureInfo.InvariantCulture, $"{JournalFile}:{entry.Sequence}");
                    return false;
                }

                previous = entry.Hash;
                lastHash = entry.Hash;
                expected++;
            }

            if (expected != closing.LastSequence + 1 || (lastHash is not null && lastHash != closing.LastEntryHash))
            {
                failure = JournalFile;
                return false;
            }

            failure = null;
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or KeyNotFoundException or FormatException or NullReferenceException)
        {
            failure = "format";
            return false;
        }
    }

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using MemoryStream copy = new();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    private static string Pem(string base64Spki) =>
        "-----BEGIN PUBLIC KEY-----\n" + string.Join('\n', base64Spki.Chunk(64).Select(c => new string(c))) + "\n-----END PUBLIC KEY-----\n";

    private const string ReadMe = """
        Archive scellée Lettra (format lettra-archive-scellee-v1)

        Contenu :
        - journal.jsonl : les entrées du journal du dossier couvertes par la clôture, une par ligne ;
        - cloture.json : la clôture signée (plage, totaux, total perpétuel, empreintes) ;
        - manifeste.json : l'empreinte SHA-256 de chaque fichier ;
        - manifeste.sig : la signature ECDSA P-256 / SHA-256 (base 64) de l'empreinte hexadécimale du manifeste ;
        - cle-publique.pem : la clé publique de signature du cabinet.

        Vérifier : 1) l'identifiant de clé (p256- suivi des 16 premiers caractères hexadécimaux du SHA-256 de la clé
        publique) figure au registre des clés du dossier que le cabinet a communiqué (les clés retirées y restent) ; 2) la signature du manifeste ; 3) l'empreinte de chaque
        fichier ; 4) chaque entrée : son empreinte recalculée et le lien vers l'entrée précédente ; 5) la dernière
        empreinte est celle de la clôture.
        """;

    private sealed record Manifest(string Format, Guid Dossier, int ClosingNumber, string ClosingHash, string KeyId, IReadOnlyList<ManifestFileEntry> Files);

    private sealed record ManifestFileEntry(string Name, string Sha256);
}
