using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lettra.Contracts.Billing;

/// <summary>
/// Forme canonique d'une pièce validée et son empreinte, inscrite au journal inaltérable (chaîne anti-fraude).
/// Le format est fixé ici, dans les contrats, pour que la vérification d'intégrité ne dépende pas de la version du
/// code qui a validé la pièce. <b>Ne jamais modifier une version publiée</b> : un changement crée la version suivante,
/// et le vérificateur choisit la version d'après le champ <c>v</c>.
/// <para>Format v1 :</para>
/// <list type="bullet">
/// <item>JSON UTF-8 sans espaces, propriétés dans l'ordre écrit par <see cref="WriteV1"/>, aucune propriété omise
/// (une valeur absente s'écrit <c>null</c>) ;</item>
/// <item>montants (totaux, bases, TVA, montant HT des lignes) en chaîne invariante à exactement 2 décimales ;
/// quantités, prix unitaires, remises et taux à exactement 4 décimales ;</item>
/// <item>lignes triées par position ; récapitulatif de TVA trié par taux décroissant puis par nom de catégorie (ordinal) ;</item>
/// <item>dates en <c>yyyy-MM-dd</c> ; identifiants en Guid minuscule avec tirets ; énumérations par leur nom (noms figés par <c>SealedFingerprintTests</c>) ;</item>
/// <item>empreinte : SHA-256 des octets UTF-8, en hexadécimal minuscule (64 caractères).</item>
/// </list>
/// Ne figurent que les données figées par la validation : ni le statut, ni la version, ni les acteurs.
/// </summary>
public static class CanonicalDocument
{
    public const int CurrentVersion = 1;

    /// <summary>Octets canoniques (v1) d'une facture, d'un acompte ou d'un avoir.</summary>
    public static byte[] ToCanonicalBytes(InvoiceResponse invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = false, SkipValidation = false }))
        {
            WriteV1(writer, invoice);
        }

        return buffer.ToArray();
    }

    /// <summary>Empreinte SHA-256 en hexadécimal minuscule de la forme canonique.</summary>
    public static string ComputeHash(InvoiceResponse invoice) =>
        Convert.ToHexStringLower(SHA256.HashData(ToCanonicalBytes(invoice)));

    /// <summary>Octets canoniques (v1) d'un devis validé : mêmes règles, champs propres au devis.</summary>
    public static byte[] ToCanonicalBytes(QuoteResponse quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", 1);
            writer.WriteString("id", Id(quote.Id));
            writer.WriteString("kind", "Quote");
            writer.WriteString("number", quote.Number);
            writer.WriteString("issueDate", Date(quote.IssueDate));
            writer.WriteString("validUntil", Date(quote.ValidUntil));
            writer.WriteString("vatRegime", quote.VatRegime.ToString());
            writer.WriteString("customerId", Id(quote.CustomerId));
            WriteParty(writer, "customer", quote.Customer);
            WriteBody(writer, quote.Lines, quote.Totals, quote.LegalMentions);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    public static string ComputeHash(QuoteResponse quote) =>
        Convert.ToHexStringLower(SHA256.HashData(ToCanonicalBytes(quote)));

    private static void WriteV1(Utf8JsonWriter w, InvoiceResponse i)
    {
        w.WriteStartObject();
        w.WriteNumber("v", 1);
        w.WriteString("id", Id(i.Id));
        w.WriteString("kind", i.Kind.ToString());
        w.WriteString("number", i.Number);
        w.WriteString("series", i.SeriesCode);
        w.WriteString("issueDate", Date(i.IssueDate));
        w.WriteString("deliveryDate", Date(i.DeliveryDate));
        w.WriteString("dueDate", Date(i.DueDate));
        w.WriteString("paymentTerms", i.PaymentTerms);
        w.WriteString("deliveryAddress", i.DeliveryAddress);
        w.WriteString("vatRegime", i.VatRegime.ToString());
        w.WriteString("operationCategory", i.OperationCategory.ToString());
        w.WriteString("purchaseOrder", i.PurchaseOrderReference);
        w.WriteString("quoteNumber", i.QuoteNumber);
        w.WriteString("creditedInvoiceId", i.CreditedInvoiceId is { } credited ? Id(credited) : null);
        w.WriteString("creditedInvoiceNumber", i.CreditedInvoiceNumber);
        w.WriteString("customerId", Id(i.CustomerId));
        WriteParty(w, "seller", i.Seller);
        WriteParty(w, "customer", i.Customer);

        WriteBody(w, i.Lines, i.Totals, i.LegalMentions);
        w.WriteEndObject();
    }

    private static void WriteBody(Utf8JsonWriter w, IReadOnlyList<DocumentLineResponse> lines, DocumentTotalsResponse totals, IReadOnlyList<string> legalMentions)
    {
        w.WriteStartArray("lines");
        foreach (DocumentLineResponse line in lines.OrderBy(l => l.Position))
        {
            w.WriteStartObject();
            w.WriteNumber("position", line.Position);
            w.WriteString("catalogItemId", line.CatalogItemId is { } item ? Id(item) : null);
            w.WriteString("reference", line.Reference);
            w.WriteString("designation", line.Designation);
            w.WriteString("description", line.Description);
            w.WriteString("kind", line.Kind.ToString());
            w.WriteString("unit", line.UnitCode);
            w.WriteString("quantity", Four(line.Quantity));
            w.WriteString("unitPrice", Four(line.UnitPriceExclTax));
            w.WriteString("discount", Four(line.DiscountPercent));
            w.WriteString("vatRateCode", line.VatRateCode);
            w.WriteString("vatRate", Four(line.VatRatePercent));
            w.WriteString("vatCategory", line.VatCategory.ToString());
            w.WriteString("exemption", line.VatExemptionCode);
            w.WriteString("totalExclTax", Two(line.TotalExclTax));
            w.WriteEndObject();
        }

        w.WriteEndArray();

        w.WriteStartArray("vat");
        foreach (VatBreakdownResponse b in totals.Breakdown.OrderByDescending(b => b.VatRatePercent).ThenBy(b => b.VatCategory.ToString(), StringComparer.Ordinal))
        {
            w.WriteStartObject();
            w.WriteString("category", b.VatCategory.ToString());
            w.WriteString("rate", Four(b.VatRatePercent));
            w.WriteString("base", Two(b.BaseExclTax));
            w.WriteString("amount", Two(b.VatAmount));
            w.WriteString("exemption", b.ExemptionCode);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteString("totalExclTax", Two(totals.TotalExclTax));
        w.WriteString("totalVat", Two(totals.TotalVat));
        w.WriteString("totalInclTax", Two(totals.TotalInclTax));

        w.WriteStartArray("legalMentions");
        foreach (string mention in legalMentions)
        {
            w.WriteStringValue(mention);
        }

        w.WriteEndArray();
    }

    private static void WriteParty(Utf8JsonWriter w, string name, PartySnapshotResponse? party)
    {
        if (party is null)
        {
            w.WriteNull(name);
            return;
        }

        w.WriteStartObject(name);
        w.WriteString("name", party.Name);
        w.WriteString("addressLine1", party.AddressLine1);
        w.WriteString("addressLine2", party.AddressLine2);
        w.WriteString("postalCode", party.PostalCode);
        w.WriteString("city", party.City);
        w.WriteString("country", party.CountryCode);
        w.WriteString("siren", party.Siren);
        w.WriteString("siret", party.Siret);
        w.WriteString("vatNumber", party.VatNumber);
        w.WriteString("legalForm", party.LegalForm);
        w.WriteString("shareCapital", party.ShareCapital is { } capital ? Two(capital) : null);
        w.WriteString("rcs", party.Rcs);
        w.WriteEndObject();
    }

    private static string Id(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    private static string? Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Two(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    private static string Four(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero).ToString("0.0000", CultureInfo.InvariantCulture);
}
