using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Serialization;
using Lettra.Domain.Billing;
using Lettra.Domain.Integrity;
using Lettra.Exports.Application.Fec;

namespace Lettra.Exports.Application.AuditKit;

/// <summary>Tout ce que le kit d'une période rassemble, lu et vérifié par <see cref="AuditKitService"/>.</summary>
public sealed record AuditKitContent(
    Guid DossierId,
    string CompanyName,
    string Siren,
    DateOnly From,
    DateOnly To,
    DateTimeOffset GeneratedAt,
    FecFile Fec,
    AuditKitAttestationResponse Attestation,
    IReadOnlyList<SealedArchive> SealedArchives,
    AuditTrailData Trail,
    IReadOnlyDictionary<Guid, byte[]> InvoiceXml,
    IReadOnlyList<JournalEntry> Journal,
    IReadOnlyDictionary<Guid, string> People);

/// <summary>Le kit prêt à remettre : son nom, l'archive ZIP et son manifeste.</summary>
public sealed record AuditKitFile(string FileName, byte[] Content, AuditKitManifestResponse Manifest);

/// <summary>
/// Écrit le kit contrôle fiscal : une archive ZIP horodatée (chaque fichier porte l'heure de préparation), dont le
/// manifeste donne l'empreinte SHA-256 de chaque autre fichier. Pur et déterministe pour un même contenu.
/// </summary>
public static class AuditKitWriter
{
    private static readonly ContractsJsonContext Json = new(new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

    public static AuditKitFile Write(AuditKitContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        IReadOnlyList<Invoice> invoices = content.Trail.Invoices;

        SortedDictionary<string, byte[]> files = new(StringComparer.Ordinal)
        {
            [AuditKitLayout.Fec(content.Fec.FileName)] = content.Fec.Content,
            [AuditKitLayout.FecReport] = JsonSerializer.SerializeToUtf8Bytes(content.Fec.Validation, Json.FecValidationResponse),
            [AuditKitLayout.Attestation] = JsonSerializer.SerializeToUtf8Bytes(content.Attestation, Json.AuditKitAttestationResponse),
            [AuditKitLayout.TrailDescription] = Encoding.UTF8.GetBytes(AuditKitTexts.TrailDescription),
            [AuditKitLayout.TrailPieces] = AuditTrailCsv.Pieces(invoices, content.Trail.Transmissions, content.People),
            [AuditKitLayout.TrailPayments] = AuditTrailCsv.Payments(content.Trail.Payments, content.People),
            [AuditKitLayout.TrailJournal] = AuditTrailCsv.Journal(content.Journal),
            [AuditKitLayout.Transmissions] = AuditTrailCsv.Transmissions(content.Trail.Transmissions),
        };

        foreach (SealedArchive archive in content.SealedArchives)
        {
            files[AuditKitLayout.Closing(archive.FileName)] = archive.Content;
        }

        foreach (Invoice invoice in invoices)
        {
            files[AuditKitLayout.XmlFile(invoice.Number!)] = content.InvoiceXml[invoice.Id.Value];
        }

        AuditKitFecSummary fec = FecSummary(content.Fec);
        AuditKitIntegritySummary integrity = IntegritySummary(content.Attestation);
        files[AuditKitLayout.ReadMe] = Encoding.UTF8.GetBytes(AuditKitTexts.ReadMe(content, fec, integrity));

        AuditKitManifestResponse manifest = new(
            AuditKitLayout.Format,
            content.DossierId,
            content.CompanyName,
            content.Siren,
            content.From,
            content.To,
            content.GeneratedAt,
            fec,
            integrity,
            [.. invoices.Select(i => new AuditKitPieceResponse(
                i.Id.Value,
                i.Number!,
                (Contracts.Billing.InvoiceKindDto)i.Kind,
                i.IssueDate!.Value,
                i.Customer.Name,
                i.TotalInclTax,
                AuditKitLayout.XmlFile(i.Number!),
                AuditKitLayout.PdfFile(i.Number!)))],
            content.Trail.Payments.Count,
            content.Trail.Transmissions.Count,
            [.. files.Select(f => new AuditKitFileResponse(f.Key, Sha256(f.Value), f.Value.LongLength))]);
        files[AuditKitLayout.Manifest] = JsonSerializer.SerializeToUtf8Bytes(manifest, Json.AuditKitManifestResponse);

        return new AuditKitFile(AuditKitLayout.FileName(content.Siren, content.From, content.To), Zip(files, content.GeneratedAt), manifest);
    }

    /// <summary>Empreinte SHA-256 en hexadécimal minuscule, comme dans le manifeste et les archives scellées.</summary>
    public static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    private static AuditKitFecSummary FecSummary(FecFile fec)
    {
        int errors = fec.Validation.Findings.Count(f => f.Severity == FecFindingSeverityDto.Error);
        return new AuditKitFecSummary(
            fec.FileName,
            fec.Validation.IsValid,
            fec.Validation.EntryCount,
            errors,
            fec.Validation.Findings.Count - errors,
            fec.Validation.TotalDebit,
            fec.Validation.TotalCredit);
    }

    private static AuditKitIntegritySummary IntegritySummary(AuditKitAttestationResponse attestation) => new(
        attestation.Report.Intact,
        attestation.Report.EntriesChecked,
        attestation.Report.ClosingsChecked,
        attestation.Report.DocumentsChecked,
        attestation.Report.Findings.Count,
        [.. attestation.Closings.Select(c => c.Number)],
        [.. attestation.Keys.Select(k => k.KeyId)]);

    private static byte[] Zip(SortedDictionary<string, byte[]> files, DateTimeOffset at)
    {
        using MemoryStream output = new();
        using (ZipArchive zip = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Le manifeste d'abord : on le lit sans parcourir toute l'archive.
            foreach ((string path, byte[] bytes) in files.OrderBy(f => f.Key == AuditKitLayout.Manifest ? 0 : 1).ThenBy(f => f.Key, StringComparer.Ordinal))
            {
                ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                entry.LastWriteTime = at;
                using Stream stream = entry.Open();
                stream.Write(bytes);
            }
        }

        return output.ToArray();
    }

    internal static string Day(DateOnly day) => day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
