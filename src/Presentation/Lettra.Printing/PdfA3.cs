using System.Globalization;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace Lettra.Printing;

/// <summary>Lien d'un fichier joint avec le document (PDF/A-3, clé <c>AFRelationship</c>).</summary>
public enum PdfAttachmentRelationship
{
    /// <summary>Données dont le document est la représentation (cas du XML Factur-X).</summary>
    Data,

    /// <summary>Représentation équivalente du document.</summary>
    Alternative,

    /// <summary>Fichier source du document.</summary>
    Source,

    /// <summary>Pièce complémentaire (bon de commande, justificatif).</summary>
    Supplement,

    Unspecified,
}

/// <summary>Fichier joint au PDF : nom, contenu, type MIME et lien avec le document.</summary>
public sealed record PdfAttachment(
    string FileName,
    ReadOnlyMemory<byte> Content,
    string MimeType,
    PdfAttachmentRelationship Relationship = PdfAttachmentRelationship.Data,
    string? Description = null)
{
    /// <summary>Nom imposé au XML d'un Factur-X.</summary>
    public const string FacturXFileName = "factur-x.xml";

    /// <summary>
    /// Le XML CII d'une facture (profil EN 16931) joint pour faire du PDF un Factur-X : le PDF/A-3 déclare alors aussi
    /// le Factur-X dans ses métadonnées XMP.
    /// </summary>
    public static PdfAttachment FacturX(ReadOnlyMemory<byte> ciiXml) =>
        new(FacturXFileName, ciiXml, "text/xml", PdfAttachmentRelationship.Data, "Factur-X");
}

/// <summary>
/// Mise en conformité PDF/A-3b d'un document PDFsharp : profil de sortie sRGB, polices incorporées et
/// métadonnées XMP (<c>SetPdfA</c>), fichiers joints déclarés comme « associated files », langue du document.
/// </summary>
internal static class PdfA3
{
    // PDFsharp écrit l'identification PDF/A-1a ; le contenu n'étant pas balisé, Lettra déclare PDF/A-3b.
    // Le remplacement garde la même longueur d'octets : la table des références croisées reste exacte.
    private static readonly (byte[] From, byte[] To)[] Identification =
    [
        (Encoding.ASCII.GetBytes("<pdfaid:part>1</pdfaid:part>"), Encoding.ASCII.GetBytes("<pdfaid:part>3</pdfaid:part>")),
        (Encoding.ASCII.GetBytes("<pdfaid:conformance>A</pdfaid:conformance>"), Encoding.ASCII.GetBytes("<pdfaid:conformance>B</pdfaid:conformance>")),
    ];

    public static byte[] Save(PdfDocument pdf, CultureInfo culture, IReadOnlyList<PdfAttachment> attachments)
    {
        pdf.SetPdfA();
        pdf.Internals.Catalog.Elements.SetString("/Lang", culture.Name);

        if (attachments.Count > 0)
        {
            PdfArray associated = new(pdf);
            foreach (PdfAttachment attachment in attachments)
            {
                associated.Elements.Add(Attach(pdf, attachment));
            }

            pdf.Internals.Catalog.Elements["/AF"] = associated;
        }

        using MemoryStream output = new();
        pdf.Save(output, false);
        byte[] bytes = output.ToArray();
        foreach ((byte[] from, byte[] to) in Identification)
        {
            ReplaceOnce(bytes, from, to);
        }

        return attachments.Any(a => a.FileName == PdfAttachment.FacturXFileName)
            ? FacturXMetadata.Declare(bytes)
            : bytes;
    }

    private static PdfReference Attach(PdfDocument pdf, PdfAttachment attachment)
    {
        pdf.AddEmbeddedFile(attachment.FileName, new MemoryStream(attachment.Content.ToArray()));

        PdfDictionary fileSpec = FindFileSpec(pdf, attachment.FileName);
        fileSpec.Elements.SetString("/UF", attachment.FileName);
        fileSpec.Elements.SetName("/AFRelationship", "/" + attachment.Relationship);
        if (attachment.Description is not null)
        {
            fileSpec.Elements.SetString("/Desc", attachment.Description);
        }

        PdfDictionary embedded = Resolve<PdfDictionary>(fileSpec.Elements.GetDictionary("/EF")!.Elements["/F"]);
        embedded.Elements.SetName("/Subtype", "/" + attachment.MimeType);
        return fileSpec.Reference ?? throw new InvalidOperationException("Spécification de fichier joint sans référence indirecte.");
    }

    private static PdfDictionary FindFileSpec(PdfDocument pdf, string fileName)
    {
        PdfArray names = pdf.Internals.Catalog.Elements.GetDictionary("/Names")?.Elements.GetDictionary("/EmbeddedFiles")?.Elements.GetArray("/Names")
            ?? throw new InvalidOperationException("Arbre des fichiers joints absent.");
        for (int i = 0; i + 1 < names.Elements.Count; i += 2)
        {
            if (names.Elements[i] is PdfString name && name.Value == fileName)
            {
                return Resolve<PdfDictionary>(names.Elements[i + 1]);
            }
        }

        throw new InvalidOperationException($"Fichier joint introuvable : {fileName}");
    }

    private static T Resolve<T>(PdfItem? item)
        where T : PdfObject =>
        (item is PdfReference reference ? reference.Value : item) as T
            ?? throw new InvalidOperationException($"Objet PDF inattendu : {item}");

    private static void ReplaceOnce(byte[] buffer, byte[] from, byte[] to)
    {
        int index = buffer.AsSpan().IndexOf(from);
        if (index < 0 || buffer.AsSpan(index + from.Length).IndexOf(from) >= 0)
        {
            throw new InvalidOperationException("Identification PDF/A inattendue dans les métadonnées XMP produites par PDFsharp.");
        }

        to.CopyTo(buffer, index);
    }
}
