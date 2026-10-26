using System.Globalization;
using Bunit;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Banking;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Billing;
using Lettra.UI.Tests.Security;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Banking;

/// <summary>Pièces justificatives : l'écran du cabinet (réclamer, déposer, dire non attendue) et l'écran « Pièces à fournir » du client.</summary>
[Collection("culture")]
public sealed class ReceiptsPagesTests : BunitContext
{
    private readonly FakeReceiptsGateway _receipts = new();
    private readonly FakeFileDownloader _files = new();
    private readonly ReceiptLineResponse _loyer = FakeReceiptsGateway.Line(17, 950m, "VIR LOYER SCI DU PORT", "SCI du Port");
    private readonly ReceiptLineResponse _energie = FakeReceiptsGateway.Line(9, 84.60m, "PRLV SEPA ENERGIE");
    private readonly ReceiptLineResponse _frais = FakeReceiptsGateway.Line(3, 8.50m, "FRAIS TENUE DE COMPTE", status: ReceiptStatusDto.NotNeeded);

    public ReceiptsPagesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IReceiptsGateway>(_receipts);
        Services.AddSingleton<IFileDownloader>(_files);
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
        _receipts.Lines.AddRange([_loyer, _energie, _frais]);
        _receipts.LastRecipient = "gerant@boulangerie-martin.example";
    }

    private IRenderedComponent<T> Open<T>() where T : Microsoft.AspNetCore.Components.IComponent
    {
        IRenderedComponent<T> page = Render<T>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    private static string[] Rows(IRenderedComponent<ReceiptsPage> page) => [.. page.FindAll("#receipts-lines tbody tr").Select(r => r.Id ?? string.Empty)];

    [Fact]
    public void Le_cabinet_voit_d_abord_les_pieces_a_obtenir_les_plus_recentes_en_tete()
    {
        IRenderedComponent<ReceiptsPage> page = Open<ReceiptsPage>();

        page.Find("#receipts-missing").TextContent.Should().Be("2");
        page.Find("#receipts-missing-amount").TextContent.Should().Contain("034,60");
        Rows(page).Should().Equal($"receipt-{_loyer.LineId}", $"receipt-{_energie.LineId}");
        page.Find($"#receipt-{_loyer.LineId}").TextContent.Should().Contain("SCI du Port").And.Contain("Manquante");

        page.Find("#receipts-status").Change("all");
        Rows(page).Should().HaveCount(3);
        page.Find("#receipts-sort-amount").Click();
        Rows(page)[0].Should().Be($"receipt-{_loyer.LineId}");
        page.Find("#receipts-search").Input("energie");
        Rows(page).Should().Equal($"receipt-{_energie.LineId}");
        page.Find("#receipts-filters-reset").Click();
        Rows(page).Should().HaveCount(2);
    }

    [Fact]
    public void Reclamer_les_pieces_choisies_envoie_un_seul_e_mail_a_la_derniere_adresse()
    {
        IRenderedComponent<ReceiptsPage> page = Open<ReceiptsPage>();

        page.Find("#receipts-request-selected").HasAttribute("disabled").Should().BeTrue();
        page.Find("#receipts-select-all").Change(true);
        page.Find("#receipts-request-selected").Click();
        page.Find("#receipts-email").GetAttribute("value").Should().Be("gerant@boulangerie-martin.example");
        page.Find("#receipts-note").Change("Pour la clôture, merci.");
        page.Find("#receipts-request-form").Submit();

        _receipts.Requests.Should().ContainSingle();
        _receipts.Requests[0].LineIds.Should().BeEquivalentTo([_loyer.LineId, _energie.LineId]);
        _receipts.Requests[0].Note.Should().Be("Pour la clôture, merci.");
        this.ToastText().Should().Be("2 pièce(s) réclamée(s) : l'e-mail est parti.");
        page.FindAll("#receipts-request-form").Should().BeEmpty();
        page.Find($"#receipt-{_loyer.LineId}").TextContent.Should().Contain("Réclamée").And.Contain("1 demande(s)");
        page.Find("#receipts-requested").TextContent.Should().Be("2");
    }

    [Fact]
    public void Sans_envoi_d_e_mails_la_page_previent_et_la_demande_reste_notee()
    {
        _receipts.EmailConfigured = false;
        IRenderedComponent<ReceiptsPage> page = Open<ReceiptsPage>();

        page.FindAll("#receipts-lines tbody input[type=checkbox]")[0].Change(true);
        page.Find("#receipts-request-selected").Click();
        page.Find("#receipts-email-off").TextContent.Should().NotBeEmpty();
        page.Find("#receipts-request-form").Submit();

        _receipts.Requests.Single().LineIds.Should().Equal(_loyer.LineId);
        this.Toasts()[^1].Kind.Should().Be(ToastKind.Warning);
    }

    [Fact]
    public void Une_depense_peut_etre_dite_non_attendue_puis_de_nouveau_attendue()
    {
        IRenderedComponent<ReceiptsPage> page = Open<ReceiptsPage>();

        page.Find($"#receipt-not-needed-{_energie.LineId}").Click();
        this.ToastText().Should().Be("Aucune pièce n'est attendue pour cette dépense.");
        Rows(page).Should().Equal($"receipt-{_loyer.LineId}");

        page.Find("#receipts-status").Change("NotNeeded");
        page.Find($"#receipt-needed-{_frais.LineId}").Click();
        this.ToastText().Should().Be("La pièce est de nouveau attendue.");
        Rows(page).Should().Equal($"receipt-{_energie.LineId}");
    }

    [Fact]
    public void Une_piece_deposee_par_le_cabinet_se_telecharge()
    {
        IRenderedComponent<ReceiptsPage> page = Open<ReceiptsPage>();

        page.FindComponents<InputFile>()[0].UploadFiles(InputFileContent.CreateFromBinary([0xFF, 0xD8, 0xFF, 0xE0], "loyer.jpg", contentType: "image/jpeg"));
        page.WaitForAssertion(() => _receipts.Uploads.Should().ContainSingle());
        _receipts.Uploads[0].LineId.Should().Be(_loyer.LineId);
        this.ToastText().Should().Be("Pièce déposée, merci.");

        page.Find("#receipts-status").Change("Received");
        page.Find($"#receipt-{_loyer.LineId}").TextContent.Should().Contain("loyer.jpg");
        page.Find($"#receipt-download-{_loyer.LineId}").Click();
        _files.Files.Should().ContainSingle().Which.FileName.Should().Be("loyer.jpg");
    }

    [Fact]
    public void Un_refus_du_serveur_s_affiche_et_la_page_reste_lisible()
    {
        _receipts.NextError = ErrorCodes.Api.Unreachable;

        IRenderedComponent<ReceiptsPage> page = Open<ReceiptsPage>();

        page.Find("#receipts-empty").Should().NotBeNull();
        this.Toasts().Should().ContainSingle().Which.Kind.Should().Be(ToastKind.Error);
    }

    [Fact]
    public void Le_client_voit_les_pieces_reclamees_les_plus_anciennes_en_tete_et_les_depose()
    {
        _receipts.Lines[0] = _loyer with { Status = ReceiptStatusDto.Requested };
        _receipts.Lines[1] = _energie with { Status = ReceiptStatusDto.Requested };
        IRenderedComponent<ReceiptsToProvidePage> page = Open<ReceiptsToProvidePage>();

        page.Find("#to-provide-count").TextContent.Should().Contain("2 pièce(s) à fournir").And.Contain("034,60");
        page.FindAll("#to-provide-list > li").Select(li => li.Id).Should().Equal($"to-provide-{_energie.LineId}", $"to-provide-{_loyer.LineId}");
        page.Find("#to-provide-search").Input("loyer");
        page.FindAll("#to-provide-list > li").Should().ContainSingle();

        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([0xFF, 0xD8, 0xFF], "photo.jpg", contentType: "image/jpeg"));
        page.WaitForAssertion(() => page.FindAll("#to-provide-list > li").Should().BeEmpty());
        page.Find("#to-provide-count").TextContent.Should().Contain("1 pièce(s)");
    }

    [Fact]
    public void Le_client_sans_piece_reclamee_voit_un_ecran_rassurant()
    {
        IRenderedComponent<ReceiptsToProvidePage> page = Open<ReceiptsToProvidePage>();

        page.Find("#to-provide-empty").TextContent.Should().Contain("Rien à fournir");
    }

    [Fact]
    public void Un_fichier_trop_lourd_est_refuse_avant_l_envoi()
    {
        _receipts.Lines[0] = _loyer with { Status = ReceiptStatusDto.Requested };
        IRenderedComponent<ReceiptsToProvidePage> page = Open<ReceiptsToProvidePage>();

        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(new byte[(5 * 1024 * 1024) + 1], "lourd.jpg", contentType: "image/jpeg"));

        _receipts.Uploads.Should().BeEmpty();
        this.Toasts()[^1].Kind.Should().Be(ToastKind.Error);
    }
}
