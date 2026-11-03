using System.Globalization;
using Bunit;
using Lettra.Contracts.Tax;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Tax;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Tax;
using Lettra.UI.Tests.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Tax;

/// <summary>
/// « Seuils » dans Déclarations et la carte « Seuils à surveiller » : l'état de chaque seuil, sa projection, ce qui change,
/// les filtres et tris, et la carte qui ne parle que des seuils proches.
/// </summary>
[Collection("culture")]
public sealed class ThresholdsPageTests : BunitContext
{
    private static readonly Guid Durand = Guid.Parse("0199a000-0000-7000-8000-0000000000d1");
    private static readonly Guid Petit = Guid.Parse("0199a000-0000-7000-8000-0000000000d2");
    private static readonly DateOnly Year = new(2026, 1, 1);
    private static readonly DateOnly YearEnd = new(2026, 12, 31);

    private readonly FakeTaxGateway _tax = new();

    public ThresholdsPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<ITaxGateway>(_tax);
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    /// <summary>L'ordre du serveur : franchi, prévu, à surveiller, loin.</summary>
    internal static ThresholdsResponse Sample(bool profileMissing = false) => new(new DateOnly(2026, 10, 9), 90, 3, profileMissing,
    [
        new("das2.beneficiaire", ThresholdFamilyDto.FeeReturn, ThresholdTierDto.Simple, Durand.ToString(), Durand, "Cabinet Durand", 2_400m, ThresholdBasisDto.InclTax,
            Year, YearEnd, 2_500m, 2_500m, 100m, ThresholdStatusDto.Crossed, Year, null, null, true, "CGI, art. 240"),
        new("tva.franchise.services.majore", ThresholdFamilyDto.VatFranchise, ThresholdTierDto.Increased, "tva.franchise.services.majore", null, null, 41_250m,
            ThresholdBasisDto.ExclTax, Year, YearEnd, 36_000m, 45_000m, 3_750m, ThresholdStatusDto.Projected, null, new(2026, 11, 20), new(2026, 11, 20), true, "CGI, art. 293 B"),
        new("vigilance.sous-traitant", ThresholdFamilyDto.SubcontractorVigilance, ThresholdTierDto.Simple, Petit.ToString(), Petit, "Maçonnerie Petit", 5_000m,
            ThresholdBasisDto.ExclTax, Year, YearEnd, 4_000m, 4_600m, 0m, ThresholdStatusDto.Near, null, null, null, false, "Code du travail, art. L. 8222-1"),
        new("taille.micro", ThresholdFamilyDto.CompanySize, ThresholdTierDto.Simple, "taille.micro", null, null, 2_000_000m,
            ThresholdBasisDto.ExclTax, new(2025, 7, 1), new(2026, 6, 30), 36_000m, 45_000m, 0m, ThresholdStatusDto.Far, null, null, null, false, "Décret n° 2008-1354"),
    ]);

    private IRenderedComponent<ThresholdsPage> Open()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("tax/thresholds");
        IRenderedComponent<ThresholdsPage> page = Render<ThresholdsPage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    private static string[] Ids(IRenderedComponent<ThresholdsPage> page) => [.. page.FindAll("#thresholds-list > li").Select(li => li.Id!)];

    /// <summary>Le texte d'un élément, espaces insécables (milliers, devise) ramenés à des espaces simples.</summary>
    private static string Text(IRenderedComponent<ThresholdsPage> page, string selector) =>
        page.Find(selector).TextContent.Replace(' ', ' ').Replace(' ', ' ');

    [Fact]
    public void Chaque_seuil_dit_ou_il_en_est_quand_il_sera_franchi_et_ce_qui_change()
    {
        _tax.Thresholds = Sample();

        IRenderedComponent<ThresholdsPage> page = Open();

        page.Find("h1").TextContent.Should().Be("Seuils à surveiller");
        page.Find("#tab-thresholds").ClassList.Should().Contain("active");
        page.Find("#thresholds-method").TextContent.Should().Contain("90 derniers jours").And.Contain("3 mois");
        page.Find("#thresholds-stat-crossed strong").TextContent.Should().Be("1");
        page.Find("#thresholds-stat-projected strong").TextContent.Should().Be("1");
        page.Find("#thresholds-stat-near strong").TextContent.Should().Be("1");
        page.Find("#thresholds-stat-far strong").TextContent.Should().Be("1");
        page.FindAll("#thresholds-profile-missing").Should().BeEmpty();
        Ids(page).Should().Equal($"threshold-das2-beneficiaire-{Durand}", "threshold-tva-franchise-services-majore", $"threshold-vigilance-sous-traitant-{Petit}", "threshold-taille-micro");

        string franchise = Text(page, "#threshold-tva-franchise-services-majore");
        franchise.Should().Contain("Franchise de TVA, services : seuil majoré").And.Contain("Prévu vers le 20 novembre 2026")
            .And.Contain("36 000 € sur 41 250 € HT").And.Contain("Projection à la fin de la période : 45 000 €, soit 3 750 € au-dessus du seuil")
            .And.Contain("Les règles changent le 20 novembre 2026.").And.Contain("Source : CGI, art. 293 B").And.Contain("Année 2026");
        page.Find("#threshold-tva-franchise-services-majore a").GetAttribute("href").Should().Be("settings/company");
        page.Find("#threshold-tva-franchise-services-majore .lt-threshold-bar").GetAttribute("aria-label").Should().Be("87 % du seuil atteint, 100 % en projection");
        page.Find("#threshold-tva-franchise-services-majore").ClassList.Should().Contain("is-warning");

        string das2 = Text(page, $"#threshold-das2-beneficiaire-{Durand}");
        das2.Should().Contain("Honoraires versés à Cabinet Durand").And.Contain("Franchi le 1 janvier 2026").And.Contain("TTC").And.NotContain("Les règles changent");
        page.Find($"#threshold-das2-beneficiaire-{Durand} a").GetAttribute("href").Should().Be("tax/fees");
        Text(page, $"#threshold-vigilance-sous-traitant-{Petit}").Should().Contain("Attestation de vigilance : Maçonnerie Petit").And.Contain("À surveiller");
        Text(page, "#threshold-taille-micro").Should().Contain("Loin du seuil").And.Contain("Du 1 juillet 2025 au 30 juin 2026");
        page.Find("#thresholds-disclaimer").TextContent.Should().Contain("sans IA");
    }

    [Fact]
    public void La_liste_se_filtre_et_se_trie()
    {
        _tax.Thresholds = Sample();
        IRenderedComponent<ThresholdsPage> page = Open();

        page.Find("#thresholds-filter-family").Change(nameof(ThresholdFamilyDto.FeeReturn));
        Ids(page).Should().Equal($"threshold-das2-beneficiaire-{Durand}");
        page.Find("#thresholds-filter-family").Change(string.Empty);
        page.Find("#thresholds-filter-status").Change(nameof(ThresholdStatusDto.Near));
        Ids(page).Should().Equal($"threshold-vigilance-sous-traitant-{Petit}");
        page.Find("#thresholds-filters-reset").Click();
        page.Find("#thresholds-search").Input("durand");
        Ids(page).Should().Equal($"threshold-das2-beneficiaire-{Durand}");
        page.Find("#thresholds-search").Input("catégorie");
        Ids(page).Should().ContainSingle().Which.Should().Be("threshold-taille-micro", "le sujet compte aussi");
        page.Find("#thresholds-search").Input("aucun");
        page.FindAll("#thresholds-filtered-empty").Should().ContainSingle();
        page.Find("#thresholds-search").Input(string.Empty);

        page.Find("#thresholds-sort").Change("Progress:Descending");
        Ids(page).Should().Equal("threshold-tva-franchise-services-majore", $"threshold-das2-beneficiaire-{Durand}", $"threshold-vigilance-sous-traitant-{Petit}", "threshold-taille-micro");
        page.Find("#thresholds-sort").Change("Name:Ascending");
        Ids(page).Should().Equal($"threshold-vigilance-sous-traitant-{Petit}", "threshold-tva-franchise-services-majore", $"threshold-das2-beneficiaire-{Durand}", "threshold-taille-micro");
        page.Find("#thresholds-sort").Change("Date:Ascending");
        Ids(page)[..2].Should().Equal($"threshold-das2-beneficiaire-{Durand}", "threshold-tva-franchise-services-majore");
    }

    [Fact]
    public void Sans_fiche_entreprise_un_bandeau_y_mene_et_sans_seuil_la_liste_est_vide()
    {
        _tax.Thresholds = Sample(profileMissing: true) with { Rows = [] };

        IRenderedComponent<ThresholdsPage> page = Open();

        page.Find("#thresholds-profile-missing a").GetAttribute("href").Should().Be("settings/company");
        page.FindAll("#thresholds-empty").Should().ContainSingle();
        page.FindAll("#thresholds-filters").Should().BeEmpty();
    }

    [Fact]
    public void Un_refus_ne_montre_ni_liste_ni_chargement()
    {
        _tax.Thresholds = null;

        IRenderedComponent<ThresholdsPage> page = Open();

        page.FindAll("#thresholds-list, #thresholds-stats, .lt-skeleton").Should().BeEmpty();
    }

    [Fact]
    public void La_carte_ne_montre_que_les_trois_seuils_proches_les_plus_urgents()
    {
        ThresholdsResponse sample = Sample();
        ThresholdWatchResponse soon = sample.Rows[1];
        _tax.Thresholds = sample with { Rows = [.. sample.Rows, soon with { Code = "tva.franchise.services.base", Subject = "b" }, soon with { Code = "taille.pme", Subject = "c" }] };

        IRenderedComponent<ThresholdsCard> card = Render<ThresholdsCard>(p => p.Add(c => c.Id, "home-thresholds"));

        card.WaitForAssertion(() => card.FindAll("#home-thresholds-list > li").Should().HaveCount(ThresholdsCard.Shown));
        card.Find("#home-thresholds-title").TextContent.Should().Contain("Seuils à surveiller");
        card.Find("#home-thresholds-all").GetAttribute("href").Should().Be("tax/thresholds");
        card.Find($"#home-thresholds-threshold-das2-beneficiaire-{Durand}").TextContent.Should().Contain("Honoraires versés à Cabinet Durand").And.Contain("Franchi");
        card.Find("#home-thresholds-threshold-tva-franchise-services-majore").TextContent
            .Replace(' ', ' ').Should().Contain("Prévu vers le 20 novembre 2026 · Les règles changent le 20 novembre 2026.").And.Contain("100 %");
        card.Find("#home-thresholds-count").TextContent.Should().Be("Seuils proches : 4");
        card.FindAll("#home-thresholds-threshold-taille-micro").Should().BeEmpty("loin du seuil");
    }

    [Fact]
    public void La_carte_se_tait_sans_seuil_proche_sur_refus_ou_sans_declarations()
    {
        _tax.Thresholds = Sample() with { Rows = [.. Sample().Rows.Where(r => !r.Soon)] };
        Render<ThresholdsCard>().Markup.Trim().Should().BeEmpty();
        _tax.Thresholds = null;
        Render<ThresholdsCard>().Markup.Trim().Should().BeEmpty();
        _tax.ThresholdReads.Should().Be(2);
    }
}
