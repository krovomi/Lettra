using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Shared.Components.Tax;

/// <summary>Le kit prêt à télécharger : l'archive complétée, son manifeste, son empreinte et le décompte des PDF.</summary>
public sealed record AssembledAuditKit(string FileName, byte[] Content, AuditKitManifestResponse Manifest, string Fingerprint, int PdfAdded, int PdfFailed);

/// <summary>
/// Complète le kit remis par le serveur avec le PDF/A-3 Factur-X de chaque pièce, rendu ici comme depuis la fiche de
/// la facture : le PDF embarque le XML CII du kit, octet pour octet. Le manifeste n'est pas réécrit : son empreinte,
/// celle du kit, reste celle calculée par le serveur.
/// </summary>
public static class AuditKitAssembler
{
    public const string ManifestFile = "manifeste.json";

    /// <summary>Le manifeste du kit, à la racine de l'archive.</summary>
    public static AuditKitManifestResponse ReadManifest(byte[] kit)
    {
        using ZipArchive zip = new(new MemoryStream(kit), ZipArchiveMode.Read);
        return Parse(Read(zip.GetEntry(ManifestFile) ?? throw new InvalidDataException(ManifestFile)));
    }

    /// <summary>
    /// <paramref name="renderPdf"/> rend le Factur-X d'une pièce à partir de son XML, ou <c>null</c> si la pièce n'a pas
    /// pu être lue. Les pièces sont traitées une à une, dans l'ordre du manifeste.
    /// </summary>
    public static async Task<AssembledAuditKit> AddPdfsAsync(
        AuditKitDocument kit,
        Func<AuditKitPieceResponse, byte[], CancellationToken, Task<byte[]?>> renderPdf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(renderPdf);
        using MemoryStream buffer = new();
        buffer.Write(kit.Content);
        AuditKitManifestResponse manifest;
        string fingerprint;
        int added = 0, failed = 0;
        using (ZipArchive zip = new(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            byte[] manifestBytes = Read(zip.GetEntry(ManifestFile) ?? throw new InvalidDataException(ManifestFile));
            manifest = Parse(manifestBytes);
            fingerprint = Convert.ToHexStringLower(SHA256.HashData(manifestBytes));

            foreach (AuditKitPieceResponse piece in manifest.Pieces)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[]? pdf = zip.GetEntry(piece.XmlFile) is { } xml ? await renderPdf(piece, Read(xml), cancellationToken) : null;
                if (pdf is null)
                {
                    failed++;
                }
                else
                {
                    ZipArchiveEntry entry = zip.CreateEntry(piece.PdfFile, CompressionLevel.Optimal);
                    entry.LastWriteTime = manifest.GeneratedAt;
                    await using Stream stream = entry.Open();
                    await stream.WriteAsync(pdf, cancellationToken);
                    added++;
                }
            }
        }

        return new AssembledAuditKit(kit.FileName, buffer.ToArray(), manifest, fingerprint, added, failed);
    }

    private static AuditKitManifestResponse Parse(byte[] manifest) =>
        JsonSerializer.Deserialize(manifest, ContractsJsonContext.Default.AuditKitManifestResponse) ?? throw new InvalidDataException(ManifestFile);

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using MemoryStream copy = new();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
