using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lettra.Printing;

/// <summary>
/// Déclaration Factur-X dans les métadonnées XMP d'un PDF/A-3 : schéma d'extension PDF/A et propriétés <c>fx:</c>
/// (type, nom du XML joint, version, profil). PDFsharp régénère ses propres métadonnées à l'enregistrement : la
/// déclaration est donc insérée dans le fichier enregistré, et la table des références croisées décalée d'autant.
/// </summary>
internal static partial class FacturXMetadata
{
    public const string Namespace = "urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#";
    public const string ConformanceLevel = "EN 16931";

    private static readonly string Declaration = $"""
              <rdf:Description rdf:about="" xmlns:pdfaExtension="http://www.aiim.org/pdfa/ns/extension/" xmlns:pdfaSchema="http://www.aiim.org/pdfa/ns/schema#" xmlns:pdfaProperty="http://www.aiim.org/pdfa/ns/property#">
                <pdfaExtension:schemas>
                  <rdf:Bag>
                    <rdf:li rdf:parseType="Resource">
                      <pdfaSchema:schema>Factur-X PDFA Extension Schema</pdfaSchema:schema>
                      <pdfaSchema:namespaceURI>{Namespace}</pdfaSchema:namespaceURI>
                      <pdfaSchema:prefix>fx</pdfaSchema:prefix>
                      <pdfaSchema:property>
                        <rdf:Seq>
            {Property("DocumentFileName", "Name of the embedded XML invoice file")}
            {Property("DocumentType", "INVOICE")}
            {Property("Version", "The actual version of the Factur-X XML schema")}
            {Property("ConformanceLevel", "The conformance level of the embedded Factur-X data")}
                        </rdf:Seq>
                      </pdfaSchema:property>
                    </rdf:li>
                  </rdf:Bag>
                </pdfaExtension:schemas>
              </rdf:Description>
              <rdf:Description rdf:about="" xmlns:fx="{Namespace}">
                <fx:DocumentType>INVOICE</fx:DocumentType>
                <fx:DocumentFileName>{PdfAttachment.FacturXFileName}</fx:DocumentFileName>
                <fx:Version>1.0</fx:Version>
                <fx:ConformanceLevel>{ConformanceLevel}</fx:ConformanceLevel>
              </rdf:Description>

        """.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static readonly byte[] MetadataHeader = Encoding.ASCII.GetBytes("/Type/Metadata/Subtype/XML/Length ");
    private static readonly byte[] RdfEnd = Encoding.ASCII.GetBytes("    </rdf:RDF>");
    private static readonly byte[] StartXref = Encoding.ASCII.GetBytes("startxref");

    public static byte[] Declare(byte[] pdf)
    {
        // Le flux XMP (non compressé, PDF/A l'exige lisible) : « /Type/Metadata/Subtype/XML/Length N>> stream ».
        int header = Single(pdf, MetadataHeader);
        int lengthStart = header + MetadataHeader.Length;
        int lengthEnd = Array.IndexOf(pdf, (byte)'>', lengthStart);
        int length = int.Parse(Encoding.ASCII.GetString(pdf, lengthStart, lengthEnd - lengthStart), CultureInfo.InvariantCulture);
        int insertAt = Single(pdf, RdfEnd);
        if (insertAt < lengthEnd)
        {
            throw new InvalidOperationException("Métadonnées XMP inattendues dans le PDF produit par PDFsharp.");
        }

        byte[] declaration = Encoding.UTF8.GetBytes(Declaration);
        byte[] newLength = Encoding.ASCII.GetBytes((length + declaration.Length).ToString(CultureInfo.InvariantCulture));
        int delta = declaration.Length + newLength.Length - (lengthEnd - lengthStart);

        using MemoryStream output = new(pdf.Length + delta);
        output.Write(pdf, 0, lengthStart);
        output.Write(newLength);
        output.Write(pdf, lengthEnd, insertAt - lengthEnd);
        output.Write(declaration);
        output.Write(pdf, insertAt, pdf.Length - insertAt);
        return ShiftCrossReferences(output.ToArray(), after: lengthStart, delta);
    }

    /// <summary>Décale de <paramref name="delta"/> les positions de la table xref et de <c>startxref</c> situées après l'insertion.</summary>
    private static byte[] ShiftCrossReferences(byte[] pdf, int after, int delta)
    {
        int startXref = pdf.AsSpan().LastIndexOf(StartXref);
        string tail = Encoding.ASCII.GetString(pdf, startXref, pdf.Length - startXref);
        Match pointer = StartXrefPattern().Match(tail);
        int xref = int.Parse(pointer.Groups["offset"].Value, CultureInfo.InvariantCulture) + delta;
        string table = Encoding.ASCII.GetString(pdf, xref, startXref - xref);
        if (!table.StartsWith("xref", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Table des références croisées introuvable dans le PDF produit par PDFsharp.");
        }

        // Chaque entrée garde ses 10 chiffres : la table ne change pas de longueur.
        string shifted = EntryPattern().Replace(table, m =>
        {
            long offset = long.Parse(m.Groups["offset"].Value, CultureInfo.InvariantCulture);
            return offset > after
                ? (offset + delta).ToString("D10", CultureInfo.InvariantCulture) + m.Groups["rest"].Value
                : m.Value;
        });
        string newTail = string.Concat(tail.AsSpan(0, pointer.Groups["offset"].Index), xref.ToString(CultureInfo.InvariantCulture), tail.AsSpan(pointer.Groups["offset"].Index + pointer.Groups["offset"].Length));

        using MemoryStream output = new(pdf.Length + 8);
        output.Write(pdf, 0, xref);
        output.Write(Encoding.ASCII.GetBytes(shifted));
        output.Write(Encoding.ASCII.GetBytes(newTail));
        return output.ToArray();
    }

    private static string Property(string name, string description) => $"""
                          <rdf:li rdf:parseType="Resource">
                            <pdfaProperty:name>{name}</pdfaProperty:name>
                            <pdfaProperty:valueType>Text</pdfaProperty:valueType>
                            <pdfaProperty:category>external</pdfaProperty:category>
                            <pdfaProperty:description>{description}</pdfaProperty:description>
                          </rdf:li>
        """;

    private static int Single(byte[] buffer, byte[] value)
    {
        int index = buffer.AsSpan().IndexOf(value);
        if (index < 0 || buffer.AsSpan(index + value.Length).IndexOf(value) >= 0)
        {
            throw new InvalidOperationException("Métadonnées XMP inattendues dans le PDF produit par PDFsharp.");
        }

        return index;
    }

    [GeneratedRegex(@"^(?<offset>\d{10})(?<rest> \d{5} n)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex EntryPattern();

    [GeneratedRegex(@"startxref\s+(?<offset>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex StartXrefPattern();
}
