using System.Xml.Linq;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Exports;
using Lettra.Exports.Application.EInvoicing;
using Lettra.Exports.Tests.Platform;

namespace Lettra.Exports.Tests;

/// <summary>
/// La période facturée (BT-73/BT-74) des factures récurrentes : <c>BillingSpecifiedPeriod</c> en CII,
/// <c>InvoicePeriod</c> en UBL, absente quand la facture n'a pas de période.
/// </summary>
public sealed class EInvoicePeriodTests
{
    private static readonly XNamespace Ram = "urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100";
    private static readonly XNamespace Udt = "urn:un:unece:uncefact:data:standard:UnqualifiedDataType:100";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    private static readonly InvoiceResponse Monthly = PlatformSamples.Invoice("FAC-000020") with
    {
        BillingPeriodStart = new DateOnly(2026, 10, 1),
        BillingPeriodEnd = new DateOnly(2026, 10, 31),
    };

    [Fact]
    public void La_periode_facturee_est_ecrite_en_CII_dans_l_ordre_du_schema()
    {
        XElement settlement = Write(Monthly, EInvoiceFormatDto.Cii).Descendants(Ram + "ApplicableHeaderTradeSettlement").Single();
        XElement period = settlement.Element(Ram + "BillingSpecifiedPeriod")!;

        period.Element(Ram + "StartDateTime")!.Element(Udt + "DateTimeString")!.Value.Should().Be("20261001");
        period.Element(Ram + "EndDateTime")!.Element(Udt + "DateTimeString")!.Value.Should().Be("20261031");
        period.ElementsBeforeSelf().Last().Name.Should().Be(Ram + "ApplicableTradeTax");
    }

    [Fact]
    public void La_periode_facturee_est_ecrite_en_UBL_avant_la_reference_de_commande()
    {
        XElement period = Write(Monthly, EInvoiceFormatDto.Ubl).Element(Cac + "InvoicePeriod")!;

        period.Element(Cbc + "StartDate")!.Value.Should().Be("2026-10-01");
        period.Element(Cbc + "EndDate")!.Value.Should().Be("2026-10-31");
        period.ElementsBeforeSelf().Last().Name.LocalName.Should().BeOneOf("DocumentCurrencyCode", "TaxCurrencyCode", "AccountingCost", "BuyerReference");
    }

    [Fact]
    public void Une_facture_sans_periode_n_en_ecrit_aucune()
    {
        InvoiceResponse plain = PlatformSamples.Invoice("FAC-000021");

        Write(plain, EInvoiceFormatDto.Cii).Descendants(Ram + "BillingSpecifiedPeriod").Should().BeEmpty();
        Write(plain, EInvoiceFormatDto.Ubl).Elements(Cac + "InvoicePeriod").Should().BeEmpty();
    }

    private static XElement Write(InvoiceResponse invoice, EInvoiceFormatDto format) =>
        XDocument.Parse(System.Text.Encoding.UTF8.GetString(EInvoiceSerializer.Write(invoice, null, format))).Root!;
}
