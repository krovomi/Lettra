using System.Globalization;
using Bunit;
using Lettra.Contracts.Identity;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Cabinet;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Cabinet;

/// <summary>Comptes intra-groupe : une carte par couple, les écarts en phrases, filtres et tri, en français et en anglais.</summary>
[Collection("culture")]
public sealed class IntercompanyPageTests : BunitContext
{
    private readonly FakeCabinetGateway _gateway = new();

    public IntercompanyPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddCabinetFakes(_gateway);
        Services.AddSingleton<IDossierSelection>(new SwitchableDossierSelection(FakeCabinetGateway.Boulangerie));
        Services.AddSingleton<ISessionGateway>(new FakeSessionGateway(new SessionResponse(true, SessionModeDto.Local, "Claire Martin", true)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private static string Pair(Guid seller, Guid buyer) => $"#ic-{seller:N}-{buyer:N}";

    [Fact]
    public void Chaque_couple_montre_ses_montants_et_ses_ecarts_en_phrases()
    {
        IRenderedComponent<IntercompanyPage> page = Render<IntercompanyPage>();
        page.WaitForAssertion(() => page.FindAll(".lt-ic-pair").Should().HaveCount(2));

        page.Find("#ic-pair-count").TextContent.Should().Be("2");
        page.Find("#ic-to-settle").TextContent.Should().Be("1");
        page.Find("#ic-subtitle").TextContent.Should().Contain("19/10/2025").And.Contain("3 dossiers");
        string gaps = page.Find(Pair(FakeCabinetGateway.Atelier, FakeCabinetGateway.Garage)).TextContent.Replace('\u202F', ' ').Replace('\u00A0', ' ');
        gaps.Should().Contain("Chez Garage du Centre, la fiche fournisseur « Atelier Éole » porte un autre IBAN")
            .And.Contain("sans virement vu dans la banque de Garage du Centre : 1.")
            .And.Contain("Le vendeur dit avoir encaissé 2 400,00")
            .And.Contain("Virement non vu").And.Contain("Virement vu").And.Contain("non encaissée");
        string clean = page.Find(Pair(FakeCabinetGateway.Boulangerie, FakeCabinetGateway.Atelier)).TextContent;
        clean.Should().Contain("Concordant").And.Contain("Atelier Éole n'a aucune opération bancaire sur la période");
    }

    [Fact]
    public void Les_filtres_et_le_tri_s_appliquent_a_l_ecran()
    {
        IRenderedComponent<IntercompanyPage> page = Render<IntercompanyPage>();
        page.WaitForAssertion(() => page.FindAll(".lt-ic-pair").Should().HaveCount(2));

        page.Find("#ic-only-gaps").Change(true);
        page.FindAll(".lt-ic-pair").Should().ContainSingle().Which.Id.Should().Be(Pair(FakeCabinetGateway.Atelier, FakeCabinetGateway.Garage)[1..]);

        page.Find("#ic-only-gaps").Change(false);
        page.Find("#ic-sort").Change("seller:Ascending");
        page.FindAll(".lt-ic-seller").Select(s => s.TextContent).Should().Equal("Atelier Éole", "Boulangerie Martin");
        page.Find("#ic-sort").Change("seller:Descending");
        page.FindAll(".lt-ic-seller").Select(s => s.TextContent).Should().Equal("Boulangerie Martin", "Atelier Éole");

        page.Find("#ic-search").Input("zzz");
        page.Find("#ic-filtered-empty").TextContent.Should().Contain("Aucun couple");
    }

    [Fact]
    public void Sans_couple_la_page_explique_quand_deux_dossiers_en_forment_un_en_anglais()
    {
        LettraCultures.Apply(CultureInfo.GetCultureInfo("en-GB"));
        _gateway.Intercompany = _gateway.Intercompany with { Pairs = [] };

        IRenderedComponent<IntercompanyPage> page = Render<IntercompanyPage>();

        page.WaitForAssertion(() => page.Find("#ic-empty").TextContent.Should().Contain("No company in your dossiers invoices another one"));
        page.Markup.Should().Contain("Intercompany accounts");
    }
}
