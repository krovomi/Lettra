using System.Globalization;
using Bunit;
using Lettra.Contracts.Billing;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Billing;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Tests.Billing;

namespace Lettra.UI.Tests.Payments;

/// <summary>Panneau « Payer par QR code » de la facture : le reste à payer, l'IBAN de la fiche entreprise, jamais pour un avoir.</summary>
[Collection("culture")]
public sealed class PaymentQrPanelTests : BunitContext
{
    private static readonly DocumentLineRequest Ligne = new()
    {
        Designation = "Conseil",
        Quantity = 2m,
        UnitCode = "HUR",
        UnitPriceExclTax = 100m,
        VatRateCode = "FR-20",
    };

    public PaymentQrPanelTests()
    {
        Services.AddLettraUi();
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<PaymentQrPanel> RenderPanel(InvoiceResponse invoice, CompanyProfileResponse? company) =>
        Render<PaymentQrPanel>(p => p.Add(c => c.Invoice, invoice).Add(c => c.Company, company));

    [Fact]
    public void Une_facture_validee_affiche_le_QR_code_du_reste_a_payer()
    {
        InvoiceResponse facture = FakeInvoicesGateway.Build(InvoiceKindDto.Invoice, InvoiceStatusDto.Validated, "F-2026-0007", Ligne)
            with
        { PaidInclTax = 40m };

        IRenderedComponent<PaymentQrPanel> panel = RenderPanel(facture, BillingFixtures.Company());

        panel.Find("#payment-qr-title").TextContent.Should().Be("Payer par QR code");
        panel.Find("#payment-qr-amount").TextContent.Should().Be(GridNumberAmount(200m));
        panel.Find("svg").GetAttribute("data-payload").Should()
            .Be("BCD\n002\n1\nSCT\nAGRIFRPP\nCabinet Martin\nFR7630006000011234567890189\nEUR200.00\n\n\nF-2026-0007");
        panel.Find("svg path").GetAttribute("d").Should().StartWith("M4,4h1v1h-1z");
    }

    [Fact]
    public void Sans_IBAN_le_panneau_renvoie_vers_la_fiche_entreprise()
    {
        InvoiceResponse facture = FakeInvoicesGateway.Build(InvoiceKindDto.Invoice, InvoiceStatusDto.Issued, "F-2026-0008", Ligne);

        IRenderedComponent<PaymentQrPanel> panel = RenderPanel(facture, BillingFixtures.Company() with { Iban = null });

        panel.FindAll("svg").Should().BeEmpty();
        panel.Find("#payment-qr-missing-iban a").GetAttribute("href").Should().Be("settings/company");
    }

    [Fact]
    public void Un_avoir_un_brouillon_ou_une_facture_soldee_n_ont_pas_de_QR_code()
    {
        RenderPanel(FakeInvoicesGateway.Build(InvoiceKindDto.CreditNote, InvoiceStatusDto.Validated, "AV-1", Ligne), BillingFixtures.Company())
            .FindAll("#payment-qr").Should().BeEmpty();
        RenderPanel(FakeInvoicesGateway.Build(InvoiceKindDto.Invoice, InvoiceStatusDto.Draft, null, Ligne), BillingFixtures.Company())
            .FindAll("#payment-qr").Should().BeEmpty();
        RenderPanel(FakeInvoicesGateway.Build(InvoiceKindDto.Invoice, InvoiceStatusDto.Issued, "F-9", Ligne) with { PaidInclTax = 240m },
            BillingFixtures.Company()).FindAll("#payment-qr").Should().BeEmpty();
    }

    [Fact]
    public void Le_panneau_suit_la_langue_de_l_ecran()
    {
        LettraCultures.Apply(CultureInfo.GetCultureInfo("en-US"));

        IRenderedComponent<PaymentQrPanel> panel = RenderPanel(
            FakeInvoicesGateway.Build(InvoiceKindDto.DownPayment, InvoiceStatusDto.Validated, "FA-1", Ligne), BillingFixtures.Company());

        panel.Find("#payment-qr-title").TextContent.Should().Be("Pay by QR code");
        panel.Markup.Should().Contain("banking app");
    }

    private static string GridNumberAmount(decimal amount) => Lettra.UI.Shared.Components.DataEntryGrid.GridNumber.FormatAmount(amount, CultureInfo.CurrentCulture);
}
