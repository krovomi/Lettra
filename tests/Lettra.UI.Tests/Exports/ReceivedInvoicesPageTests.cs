using System.Globalization;
using Bunit;
using Lettra.Contracts.Exports;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Parties;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Billing;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Exports;

/// <summary>
/// L'écran des factures reçues : inscription à l'annuaire et dates de la réforme, relevé à la plateforme, filtres et
/// tri, détail d'une facture, réponses de l'acheteur (motif d'un litige ou d'un refus, montant d'un paiement transmis).
/// </summary>
[Collection("culture")]
public sealed class ReceivedInvoicesPageTests : BunitContext
{
    private readonly FakeReceivedInvoicesGateway _gateway = new();
    private readonly FakeFileDownloader _files = new();

    public ReceivedInvoicesPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IReceivedInvoicesGateway>(_gateway);
        Services.AddSingleton<IFileDownloader>(_files);
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<ReceivedInvoicesPage> Open()
    {
        IRenderedComponent<ReceivedInvoicesPage> page = Render<ReceivedInvoicesPage>();
        page.WaitForAssertion(() => page.FindAll("#reception-setup").Should().ContainSingle());
        return page;
    }

    [Fact]
    public void L_ecran_dit_l_inscription_a_l_annuaire_et_les_dates_de_la_reforme()
    {
        IRenderedComponent<ReceivedInvoicesPage> page = Open();

        page.Find("#reception-directory").TextContent.Should().Contain("0225:552032534").And.Contain("Plateforme agréée simulée").And.Contain("01/06/2026");
        page.Find("#reception-dates").TextContent.Should().Contain("01/09/2026").And.Contain("01/09/2027");
        page.Find("#reception-empty").TextContent.Should().Contain("Aucune facture reçue");
        page.Find("#reception-fetch").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Sans_plateforme_le_releve_est_impossible_et_l_ecran_le_dit()
    {
        _gateway.PlatformConfigured = false;

        IRenderedComponent<ReceivedInvoicesPage> page = Open();

        page.Find("#reception-no-platform").TextContent.Should().Contain("Aucune plateforme agréée n'est configurée");
        page.Find("#reception-fetch").HasAttribute("disabled").Should().BeTrue();
        page.FindAll("#reception-directory").Should().BeEmpty();
    }

    [Fact]
    public void Le_releve_ajoute_les_factures_et_le_dit_dans_le_toaster()
    {
        _gateway.Pending.Add(FakeReceivedInvoicesGateway.Invoice("PM-2026-0412"));
        _gateway.Pending.Add(FakeReceivedInvoicesGateway.Invoice("TL-26-1187", "Transports Lefèvre", "793481268", 250m, issued: new DateOnly(2026, 10, 21)));
        IRenderedComponent<ReceivedInvoicesPage> page = Open();

        page.Find("#reception-fetch").Click();

        page.WaitForAssertion(() => page.FindAll("#reception-invoices tbody tr").Should().HaveCount(2));
        this.ToastText().Should().Be("2 facture(s) reçue(s).");
        page.Find("#reception-to-handle").TextContent.Should().Contain("2");
        page.FindAll("#reception-invoices tbody tr")[0].TextContent.Should().Contain("Transports Lefèvre", "la plus récente d'abord");

        page.Find("#reception-fetch").Click();
        page.WaitForAssertion(() => this.ToastText().Should().Be("Aucune nouvelle facture."));
    }

    [Fact]
    public void La_liste_se_filtre_par_texte_et_par_statut()
    {
        _gateway.Received.Add(FakeReceivedInvoicesGateway.Invoice("PM-2026-0412"));
        _gateway.Received.Add(FakeReceivedInvoicesGateway.Invoice("TL-26-1187", "Transports Lefèvre", "793481268", 250m, status: PlatformStatusDto.Approved));
        IRenderedComponent<ReceivedInvoicesPage> page = Open();
        page.WaitForAssertion(() => page.FindAll("#reception-invoices tbody tr").Should().HaveCount(2));

        page.Find("#reception-search").Input("lefevre");
        page.WaitForAssertion(() => page.FindAll("#reception-invoices tbody tr").Should().ContainSingle().Which.TextContent.Should().Contain("TL-26-1187"));

        page.Find("#reception-search").Input(string.Empty);
        page.Find("#reception-status").Change("to-handle");
        page.WaitForAssertion(() => page.FindAll("#reception-invoices tbody tr").Should().ContainSingle().Which.TextContent.Should().Contain("PM-2026-0412"));

        page.Find("#reception-status").Change(PlatformStatusDto.Refused.ToString());
        page.WaitForAssertion(() => page.FindAll("#reception-filtered-empty").Should().ContainSingle());
        page.Find("#reception-empty-reset").Click();
        page.WaitForAssertion(() => page.FindAll("#reception-invoices tbody tr").Should().HaveCount(2));
    }

    [Fact]
    public void Approuver_puis_transmettre_le_paiement_d_une_facture()
    {
        ReceivedInvoiceResponse invoice = FakeReceivedInvoicesGateway.Invoice("PM-2026-0412");
        _gateway.Received.Add(invoice);
        IRenderedComponent<ReceivedInvoicesPage> page = Open();

        page.WaitForAssertion(() => page.FindAll($"#received-open-{invoice.Summary.Id}").Should().ContainSingle());
        page.Find($"#received-open-{invoice.Summary.Id}").Click();
        page.WaitForAssertion(() => page.Find("#reception-detail-title").TextContent.Should().Contain("PM-2026-0412").And.Contain("Papeterie Moreau SARL"));
        page.Find("#reception-lines").TextContent.Should().Contain("Ramettes de papier A4 80 g");
        page.Find("#reception-response").GetAttribute("value").Should().Be(PlatformStatusDto.Approved.ToString(), "approuver est proposé d'abord");

        page.Find("#reception-respond").Submit();
        page.WaitForAssertion(() => this.ToastText().Should().Be("Réponse « Approuvée » envoyée pour la facture PM-2026-0412."));
        page.Find("#reception-history").TextContent.Should().Contain("Approuvée");

        page.Find("#reception-response").Change(PlatformStatusDto.PaymentSent.ToString());
        page.Find("#reception-amount").GetAttribute("value").Should().Be("164,40", "le TTC est proposé");
        page.Find("#reception-respond").Submit();

        page.WaitForAssertion(() => _gateway.Responses.Select(r => r.Status).Should().Equal(PlatformStatusDto.Approved, PlatformStatusDto.PaymentSent));
        _gateway.Responses[^1].Amount.Should().Be(164.40m);
    }

    [Fact]
    public void Un_refus_se_motive_et_clot_la_facture()
    {
        ReceivedInvoiceResponse invoice = FakeReceivedInvoicesGateway.Invoice("TL-26-1187", "Transports Lefèvre", "793481268", 250m);
        _gateway.Received.Add(invoice);
        IRenderedComponent<ReceivedInvoicesPage> page = Open();
        page.WaitForAssertion(() => page.FindAll($"#received-open-{invoice.Summary.Id}").Should().ContainSingle());
        page.Find($"#received-open-{invoice.Summary.Id}").Click();
        page.WaitForAssertion(() => page.Find("#reception-response"));

        page.Find("#reception-response").Change(PlatformStatusDto.Refused.ToString());
        page.Find("#reception-refuse-final").TextContent.Should().NotBeNullOrWhiteSpace();
        page.Find("#reception-respond").Submit();
        page.WaitForAssertion(() => this.ToastText().Should().Be("Un litige ou un refus doit être motivé."));
        _gateway.Responses.Should().BeEmpty();

        page.Find("#reception-reason").Change("Livraison jamais reçue");
        page.Find("#reception-respond").Submit();

        page.WaitForAssertion(() => page.FindAll("#reception-respond").Should().BeEmpty("un refus est définitif"));
        _gateway.Responses.Should().ContainSingle().Which.Reason.Should().Be("Livraison jamais reçue");
    }

    [Fact]
    public void Un_avoir_ne_propose_pas_de_paiement_et_le_fichier_se_telecharge()
    {
        ReceivedInvoiceResponse creditNote = FakeReceivedInvoicesGateway.Invoice("PM-2026-0430", kind: ReceivedInvoiceKindDto.CreditNote, status: PlatformStatusDto.Approved);
        _gateway.Received.Add(creditNote);
        IRenderedComponent<ReceivedInvoicesPage> page = Open();
        page.WaitForAssertion(() => page.FindAll($"#received-open-{creditNote.Summary.Id}").Should().ContainSingle());
        page.Find($"#received-open-{creditNote.Summary.Id}").Click();
        page.WaitForAssertion(() => page.Find("#reception-response"));

        page.FindAll("#reception-response option").Select(o => o.GetAttribute("value")).Should().NotContain(PlatformStatusDto.PaymentSent.ToString());

        page.Find("#reception-download").Click();
        page.WaitForAssertion(() => _files.Files.Should().ContainSingle().Which.FileName.Should().Be("PM-2026-0430.xml"));

        page.Find("#reception-close").Click();
        page.FindAll("#reception-detail").Should().BeEmpty();
    }

    [Fact]
    public void L_ecran_s_affiche_en_anglais()
    {
        LettraCultures.Apply(CultureInfo.GetCultureInfo("en-US"));
        _gateway.Received.Add(FakeReceivedInvoicesGateway.Invoice("PM-2026-0412"));

        IRenderedComponent<ReceivedInvoicesPage> page = Open();

        page.Find("h1").TextContent.Should().Be("Received invoices");
        page.Find("#reception-dates").TextContent.Should().Contain("9/1/2026");
    }
}
