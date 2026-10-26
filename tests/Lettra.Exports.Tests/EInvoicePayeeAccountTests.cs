using System.Xml.Linq;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Exports;
using Lettra.Exports.Application.EInvoicing;
using Lettra.Exports.Tests.Platform;

namespace Lettra.Exports.Tests;

/// <summary>
/// Le nom du compte de paiement (BT-85) : le titulaire exact de l'IBAN, quand il diffère du vendeur, pour que la banque
/// de l'acheteur réponde « correspond » à la vérification du bénéficiaire. En CII comme en UBL ; jamais sur un avoir.
/// </summary>
public sealed class EInvoicePayeeAccountTests
{
    private static readonly XNamespace Ram = "urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    private static CompanyProfileResponse Profile(string? holder) => new(
        1, "Conseil Lettra", "SAS", null, "552100554", null, null, "FR96552100554", "10 avenue de l'Opéra", null, "75001", "Paris", "FR",
        false, false, 30, null, null, null, "FR7630006000011234567890189", "AGRIFRPP", DateTimeOffset.UnixEpoch, AccountHolderName: holder);

    [Fact]
    public void Le_titulaire_du_compte_accompagne_l_iban_en_cii_et_en_ubl()
    {
        InvoiceResponse invoice = PlatformSamples.Invoice("FAC-000020");

        XElement cii = Root(invoice, Profile("Lettra Conseil"), EInvoiceFormatDto.Cii);
        XElement account = cii.Descendants(Ram + "PayeePartyCreditorFinancialAccount").Single();
        account.Elements().Select(e => e.Name.LocalName).Should().Equal("IBANID", "AccountName");
        account.Element(Ram + "AccountName")!.Value.Should().Be("Lettra Conseil");

        XElement ubl = Root(invoice, Profile("Lettra Conseil"), EInvoiceFormatDto.Ubl);
        XElement payee = ubl.Descendants(Cac + "PayeeFinancialAccount").Single();
        payee.Elements().Select(e => e.Name.LocalName).Should().Equal("ID", "Name", "FinancialInstitutionBranch");
        payee.Element(Cbc + "Name")!.Value.Should().Be("Lettra Conseil");
    }

    [Fact]
    public void Sans_titulaire_distinct_le_nom_du_compte_n_est_pas_ecrit()
    {
        InvoiceResponse invoice = PlatformSamples.Invoice("FAC-000021");

        Root(invoice, Profile(null), EInvoiceFormatDto.Cii).Descendants(Ram + "AccountName").Should().BeEmpty();
        Root(invoice, Profile(null), EInvoiceFormatDto.Ubl).Descendants(Cac + "PayeeFinancialAccount").Single()
            .Element(Cbc + "Name").Should().BeNull();
    }

    [Fact]
    public void Un_avoir_ne_porte_ni_iban_ni_titulaire() =>
        Root(PlatformSamples.Invoice("AV-000020", InvoiceKindDto.CreditNote), Profile("Lettra Conseil"), EInvoiceFormatDto.Cii, new DateOnly(2026, 9, 30))
            .Descendants(Ram + "PayeePartyCreditorFinancialAccount").Should().BeEmpty();

    private static XElement Root(InvoiceResponse invoice, CompanyProfileResponse profile, EInvoiceFormatDto format, DateOnly? original = null) =>
        XDocument.Parse(System.Text.Encoding.UTF8.GetString(EInvoiceSerializer.Write(invoice, profile, format, original))).Root!;
}
