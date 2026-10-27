using System.Xml.Linq;
using Lettra.Calculations.Compliance;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Exports;
using Lettra.Exports.Application.EInvoicing;
using Lettra.Exports.Tests.Platform;

namespace Lettra.Exports.Tests;

/// <summary>
/// Les données propres à la réforme : le cadre de facturation (BT-23 : B1, S1, M1, ou B4, S4, M4 pour la facture
/// définitive après acompte), écrit en CII dans <c>BusinessProcessSpecifiedDocumentContextParameter</c> et en UBL dans
/// <c>ProfileID</c> ; l'option pour la TVA d'après les débits (BT-8), code 5 en CII et 3 en UBL.
/// </summary>
public sealed class EInvoiceBusinessProcessTests
{
    private static readonly XNamespace Rsm = "urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100";
    private static readonly XNamespace Ram = "urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    [Theory]
    [InlineData(OperationCategoryDto.Services, "S1")]
    [InlineData(OperationCategoryDto.Goods, "B1")]
    [InlineData(OperationCategoryDto.Mixed, "M1")]
    public void Le_cadre_de_facturation_suit_la_categorie_d_operation(OperationCategoryDto category, string expected)
    {
        InvoiceResponse invoice = PlatformSamples.Invoice("FAC-000030") with { OperationCategory = category };

        Process(Write(invoice, EInvoiceFormatDto.Cii), EInvoiceFormatDto.Cii).Should().Be(expected);
        Process(Write(invoice, EInvoiceFormatDto.Ubl), EInvoiceFormatDto.Ubl).Should().Be(expected);
    }

    [Fact]
    public void La_facture_definitive_qui_deduit_un_acompte_est_en_cadre_4()
    {
        InvoiceResponse plain = PlatformSamples.Invoice("FAC-000031");
        DocumentLineResponse deduction = plain.Lines[0] with { Position = 2, Designation = "Acompte déjà facturé", Quantity = -1m, UnitPriceExclTax = 90m, TotalExclTax = -90m };
        InvoiceResponse final = plain with { QuoteId = Guid.CreateVersion7(), Lines = [.. plain.Lines, deduction] };

        EInvoice.BusinessProcessOf(final).Should().Be("S4");
        EInvoice.BusinessProcessOf(final with { QuoteId = null }).Should().Be("S1", "sans devis, une ligne négative n'est pas un acompte déduit");
        EInvoice.BusinessProcessOf(final with { Kind = InvoiceKindDto.CreditNote }).Should().Be("S1");
        EInvoice.BusinessProcessOf(plain with { QuoteId = Guid.CreateVersion7() }).Should().Be("S1");
    }

    [Fact]
    public void Le_cadre_de_facturation_est_ecrit_avant_le_profil_en_CII()
    {
        XElement context = Write(PlatformSamples.Invoice("FAC-000032"), EInvoiceFormatDto.Cii).Element(Rsm + "ExchangedDocumentContext")!;

        context.Elements().Select(e => e.Name.LocalName).Should().Equal(
            "BusinessProcessSpecifiedDocumentContextParameter", "GuidelineSpecifiedDocumentContextParameter");
    }

    [Fact]
    public void L_option_sur_les_debits_est_ecrite_en_CII_et_en_UBL()
    {
        InvoiceResponse invoice = PlatformSamples.Invoice("FAC-000033") with { LegalMentions = [LegalMentionTexts.VatOnDebits] };

        XElement tax = Write(invoice, EInvoiceFormatDto.Cii).Descendants(Ram + "ApplicableHeaderTradeSettlement").Single().Element(Ram + "ApplicableTradeTax")!;
        tax.Element(Ram + "DueDateTypeCode")!.Value.Should().Be("5");
        tax.Element(Ram + "DueDateTypeCode")!.ElementsAfterSelf().Select(e => e.Name.LocalName).Should().Equal("RateApplicablePercent");

        XElement period = Write(invoice, EInvoiceFormatDto.Ubl).Element(Cac + "InvoicePeriod")!;
        period.Elements().Select(e => e.Name.LocalName).Should().Equal("DescriptionCode");
        period.Element(Cbc + "DescriptionCode")!.Value.Should().Be("3");
    }

    [Fact]
    public void L_option_sur_les_debits_s_ajoute_a_la_periode_facturee_en_UBL()
    {
        InvoiceResponse invoice = PlatformSamples.Invoice("FAC-000034") with
        {
            LegalMentions = [LegalMentionTexts.VatOnDebits],
            BillingPeriodStart = new DateOnly(2026, 10, 1),
            BillingPeriodEnd = new DateOnly(2026, 10, 31),
        };

        Write(invoice, EInvoiceFormatDto.Ubl).Element(Cac + "InvoicePeriod")!.Elements().Select(e => e.Name.LocalName)
            .Should().Equal("StartDate", "EndDate", "DescriptionCode");
    }

    [Fact]
    public void Sans_option_sur_les_debits_aucun_code_d_exigibilite()
    {
        InvoiceResponse invoice = PlatformSamples.Invoice("FAC-000035");

        Write(invoice, EInvoiceFormatDto.Cii).Descendants(Ram + "DueDateTypeCode").Should().BeEmpty();
        Write(invoice, EInvoiceFormatDto.Ubl).Elements(Cac + "InvoicePeriod").Should().BeEmpty();
    }

    private static string Process(XElement root, EInvoiceFormatDto format) => format == EInvoiceFormatDto.Cii
        ? root.Element(Rsm + "ExchangedDocumentContext")!.Element(Ram + "BusinessProcessSpecifiedDocumentContextParameter")!.Element(Ram + "ID")!.Value
        : root.Element(Cbc + "ProfileID")!.Value;

    private static XElement Write(InvoiceResponse invoice, EInvoiceFormatDto format) =>
        XDocument.Parse(System.Text.Encoding.UTF8.GetString(EInvoiceSerializer.Write(invoice, null, format))).Root!;
}
