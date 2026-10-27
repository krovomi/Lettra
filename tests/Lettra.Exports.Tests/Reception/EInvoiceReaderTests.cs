using System.Text;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Exports;
using Lettra.Exports.Application.EInvoicing;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Exports.Application.EInvoicing.Reception;
using Lettra.Exports.Domain.Platform;
using Lettra.Exports.Domain.Reception;
using Lettra.Exports.Infrastructure;
using Lettra.Exports.Tests.Platform;

namespace Lettra.Exports.Tests.Reception;

/// <summary>
/// Relecture des factures électroniques reçues : nos propres fichiers CII et UBL (facture et avoir) se relisent à
/// l'identique ; les factures fictives de la plateforme simulée se lisent toutes ; un fichier mal formé, sans numéro,
/// au mauvais format ou porteur d'une DTD est refusé.
/// </summary>
public sealed class EInvoiceReaderTests
{
    [Theory]
    [InlineData(EInvoiceFormatDto.Cii)]
    [InlineData(EInvoiceFormatDto.Ubl)]
    public void Une_facture_ecrite_par_Lettra_se_relit_a_l_identique(EInvoiceFormatDto format)
    {
        InvoiceResponse invoice = PlatformSamples.Invoice("FAC-000040");

        ReadEInvoice read = Read(format, EInvoiceSerializer.Write(invoice, null, format))!;

        read.Header.Should().Be(new ReceivedInvoiceHeader(
            ReceivedInvoiceKind.Invoice, "FAC-000040", new DateOnly(2026, 10, 15), new DateOnly(2026, 11, 14), "Conseil Fictif",
            PlatformSamples.SellerSiren, "FR96552100554", PlatformSamples.BuyerSiren, "EUR", 300m, 60m, 360m, "S1"));
        read.Lines.Should().ContainSingle().Which.Should().Be(new ReceivedInvoiceLineResponse("Conseil", 3m, "HUR", 100m, 300m, "S", 20m));
        read.Vat.Should().ContainSingle().Which.Should().Be(new ReceivedInvoiceVatResponse("S", 20m, 300m, 60m));
        read.Notes.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(EInvoiceFormatDto.Cii)]
    [InlineData(EInvoiceFormatDto.Ubl)]
    public void Un_avoir_se_relit_comme_un_avoir(EInvoiceFormatDto format)
    {
        InvoiceResponse credit = PlatformSamples.Invoice("AV-000001", InvoiceKindDto.CreditNote);

        ReadEInvoice read = Read(format, EInvoiceSerializer.Write(credit, null, format))!;

        read.Header.Kind.Should().Be(ReceivedInvoiceKind.CreditNote);
        read.Header.DueDate.Should().BeNull();
        read.Header.TotalInclTax.Should().Be(360m);
    }

    [Fact]
    public void Les_factures_fictives_de_la_plateforme_simulee_se_lisent_toutes()
    {
        IReadOnlyList<PlatformIncomingInvoice> files = SimulatedSupplierInvoices.For(PlatformSamples.BuyerSiren, "Atelier Durand", new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero));

        files.Should().HaveCount(4);
        files.Select(f => f.Reference).Should().OnlyHaveUniqueItems().And.AllSatisfy(r => r.Should().StartWith($"PAS-R-{PlatformSamples.BuyerSiren}-"));
        ReadEInvoice[] read = [.. files.Select(f => EInvoiceReader.Read((EInvoiceFormat)(int)f.Format, f.Content)!)];
        read.Should().AllSatisfy(r =>
        {
            r.Header.BuyerSiren.Should().Be(PlatformSamples.BuyerSiren);
            r.Header.TotalInclTax.Should().Be(r.Header.TotalExclTax + r.Header.TotalVat);
            r.Lines.Sum(l => l.NetAmount).Should().Be(r.Header.TotalExclTax);
            r.Header.BusinessProcess.Should().MatchRegex("^[BSM][14]$");
        });
        read.Select(r => r.Header.Kind).Should().Equal(ReceivedInvoiceKind.Invoice, ReceivedInvoiceKind.Invoice, ReceivedInvoiceKind.Invoice, ReceivedInvoiceKind.CreditNote);
        read[0].Header.IssueDate.Should().Be(new DateOnly(2026, 10, 18));
    }

    [Theory]
    [InlineData("<pas-une-facture/>")]
    [InlineData("pas du XML")]
    [InlineData("""<?xml version="1.0"?><!DOCTYPE x [<!ENTITY e SYSTEM "file:///etc/passwd">]><x>&e;</x>""")]
    public void Un_fichier_qui_n_est_pas_une_facture_est_refuse(string content)
    {
        Read(EInvoiceFormatDto.Cii, Encoding.UTF8.GetBytes(content)).Should().BeNull();
        Read(EInvoiceFormatDto.Ubl, Encoding.UTF8.GetBytes(content)).Should().BeNull();
    }

    [Fact]
    public void Un_fichier_au_mauvais_format_ou_sans_numero_est_refuse()
    {
        byte[] cii = EInvoiceSerializer.Write(PlatformSamples.Invoice("FAC-000041"), null, EInvoiceFormatDto.Cii);
        Read(EInvoiceFormatDto.Ubl, cii).Should().BeNull();

        string withoutNumber = Encoding.UTF8.GetString(cii).Replace("<ram:ID>FAC-000041</ram:ID>", "<ram:ID></ram:ID>", StringComparison.Ordinal);
        withoutNumber.Should().NotBe(Encoding.UTF8.GetString(cii));
        Read(EInvoiceFormatDto.Cii, Encoding.UTF8.GetBytes(withoutNumber)).Should().BeNull();

        string badAmount = Encoding.UTF8.GetString(cii).Replace(">360.00<", ">trois cents<", StringComparison.Ordinal);
        Read(EInvoiceFormatDto.Cii, Encoding.UTF8.GetBytes(badAmount)).Should().BeNull();
    }

    private static ReadEInvoice? Read(EInvoiceFormatDto format, byte[] content) => EInvoiceReader.Read((EInvoiceFormat)(int)format, content);
}
