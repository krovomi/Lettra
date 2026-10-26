using System.Globalization;
using Bunit;
using Lettra.Contracts.Insights;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Insights;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Billing;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Insights;

/// <summary>Écran « Vérifier une facture » : le texte du QR code, le verdict, les écarts et le registre des clés publiques.</summary>
[Collection("culture")]
public sealed class InvoiceProofPageTests : BunitContext
{
    private const string Code = "LETTRA-PREUVE-1;552100554;FA-2026-0042;20261016;426.94;p256-0123456789abcdef;MEUCIQ";
    private readonly FakeInsightsGateway _insights = new();
    private readonly FakeFileDownloader _files = new();

    public InvoiceProofPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IInsightsGateway>(_insights);
        Services.AddSingleton<IFileDownloader>(_files);
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private static InvoiceProofCheckResponse Check(InvoiceProofVerdictDto verdict, Guid? invoice = null, params string[] differences) =>
        new(verdict, "552100554", "FA-2026-0042", new DateOnly(2026, 10, 16), 426.94m, "p256-0123456789abcdef", invoice, differences);

    private IRenderedComponent<InvoiceProofPage> Verify(InvoiceProofCheckResponse check)
    {
        _insights.Check = check;
        IRenderedComponent<InvoiceProofPage> page = Render<InvoiceProofPage>();
        page.Find("#proof-verify").HasAttribute("disabled").Should().BeTrue("rien à vérifier sans texte");
        page.Find("#proof-code").Input($"  {Code}\n");
        page.Find("#proof-verify").Click();
        return page;
    }

    [Fact]
    public void Une_facture_authentique_montre_ce_que_dit_la_preuve_et_mene_a_la_piece()
    {
        Guid invoice = Guid.CreateVersion7();

        IRenderedComponent<InvoiceProofPage> page = Verify(Check(InvoiceProofVerdictDto.Authentic, invoice));

        _insights.Verified.Should().Equal(Code);
        page.Find("#proof-verdict").ClassList.Should().Contain("is-success");
        page.Find("#proof-verdict").TextContent.Should().Contain("Facture authentique");
        page.Find("#proof-facts").TextContent.Should().Contain("552100554").And.Contain("FA-2026-0042").And.Contain("16/10/2026").And.Contain("426,94").And.Contain("p256-0123456789abcdef");
        page.Find("#proof-open").GetAttribute("href").Should().Be($"invoices/{invoice}");
        page.FindAll("#proof-differences").Should().BeEmpty();
    }

    [Theory]
    [InlineData(InvoiceProofVerdictDto.Forged, "is-danger", "Signature invalide")]
    [InlineData(InvoiceProofVerdictDto.Foreign, "is-danger", "n'a pas été émise par ce dossier")]
    [InlineData(InvoiceProofVerdictDto.Unreadable, "is-warning", "n'est pas une preuve Lettra")]
    public void Une_preuve_refusee_dit_pourquoi(InvoiceProofVerdictDto verdict, string css, string text)
    {
        IRenderedComponent<InvoiceProofPage> page = Verify(Check(verdict));

        page.Find("#proof-verdict").ClassList.Should().Contain(css);
        page.Find("#proof-verdict").TextContent.Should().Contain(text);
        page.FindAll("#proof-open").Should().BeEmpty();
        page.FindAll("#proof-facts").Should().HaveCount(verdict == InvoiceProofVerdictDto.Unreadable ? 0 : 1);
    }

    [Fact]
    public void Les_ecarts_avec_le_dossier_et_l_annulation_par_avoir_sont_listes()
    {
        IRenderedComponent<InvoiceProofPage> page = Verify(Check(InvoiceProofVerdictDto.Mismatch, Guid.CreateVersion7(), "IssueDate", "TotalInclTax") with { FullyCredited = true });

        page.FindAll("#proof-differences li").Select(l => l.TextContent).Should().Equal("La date d'émission n'est pas la même.", "Le total TTC n'est pas le même.");
        page.Find("#proof-credited").Should().NotBeNull();
    }

    [Fact]
    public void Le_registre_des_cles_se_lit_et_se_telecharge_au_format_pem()
    {
        _insights.Keys =
        [
            new(1, "p256-aaaaaaaaaaaaaaaa", "-----BEGIN PUBLIC KEY-----\nAAA\n-----END PUBLIC KEY-----\n", new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero)),
            new(2, "p256-bbbbbbbbbbbbbbbb", "-----BEGIN PUBLIC KEY-----\nBBB\n-----END PUBLIC KEY-----\n", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), null),
        ];
        IRenderedComponent<InvoiceProofPage> page = Render<InvoiceProofPage>();

        page.FindAll("#proof-keys li").Should().HaveCount(2);
        page.Find("#proof-keys").TextContent.Should().Contain("(vérification seule)").And.Contain("en vigueur depuis le");
        page.Markup.Should().Contain("openssl dgst -sha256 -verify");
        page.FindAll("#proof-keys button")[1].Click();
        _files.Files.Should().ContainSingle().Which.FileName.Should().Be("p256-bbbbbbbbbbbbbbbb.pem");

        _insights.Keys = [];
        Render<InvoiceProofPage>().FindAll("#proof-keys-empty").Should().ContainSingle();
    }

    [Fact]
    public void Un_refus_du_serveur_s_affiche_et_l_ecran_existe_en_anglais()
    {
        _insights.Keys = null;
        Render<InvoiceProofPage>();
        this.Toasts().Should().ContainSingle().Which.Kind.Should().Be(ToastKind.Error);

        LettraCultures.Apply(CultureInfo.GetCultureInfo("en-US"));
        _insights.Keys = [];
        IRenderedComponent<InvoiceProofPage> page = Verify(Check(InvoiceProofVerdictDto.Authentic));
        page.Find("h1").TextContent.Should().Be("Check an invoice");
        page.Find("#proof-verdict").TextContent.Should().Contain("Genuine invoice");
    }
}
