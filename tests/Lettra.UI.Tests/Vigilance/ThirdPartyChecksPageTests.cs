using System.Globalization;
using Bunit;
using Lettra.Contracts.Common;
using Lettra.Contracts.Vigilance;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Parties;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Vigilance;

/// <summary>« Vérifier les tiers » : clients et fournisseurs passés aux registres officiels, ceux à examiner d'abord.</summary>
[Collection("culture")]
public sealed class ThirdPartyChecksPageTests : BunitContext
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 10, 0, 0, TimeSpan.Zero);
    private readonly FakeThirdPartyChecksGateway _checks = new(Now);

    public ThirdPartyChecksPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IThirdPartyChecksGateway>(_checks);
        Services.AddSingleton<TimeProvider>(new ManualClock(Now));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<ThirdPartyChecksPage> Open()
    {
        IRenderedComponent<ThirdPartyChecksPage> page = Render<ThirdPartyChecksPage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    private static string[] Names(IRenderedComponent<ThirdPartyChecksPage> page) =>
        [.. page.FindAll("#checks-table tbody tr td:first-child a").Select(c => c.TextContent.Trim())];

    [Fact]
    public void Les_tiers_a_examiner_viennent_d_abord_et_se_filtrent()
    {
        _checks.Add("Boulangerie Martin", status: ThirdPartyCheckStatusDto.Clear, checkedAt: Now.AddDays(-3));
        _checks.Rows.Add(new ThirdPartyCheckRow(ThirdPartyKindDto.Supplier, Guid.CreateVersion7(), "Import Lumière", "542051180",
            ThirdPartyCheckStatusDto.ToReview, Now.AddDays(-1), 1, FreezeScreeningDto.PossibleMatch, "Viktor Fictivenko",
            LegalNoticeKindDto.CollectiveProceeding, new DateOnly(2026, 9, 15), "Redressement judiciaire", UrssafAnswerDto.NotRecognized));
        _checks.Add("Atelier Zola", siren: null);
        _checks.Add("Zinc et fils", ThirdPartyKindDto.Supplier, status: ThirdPartyCheckStatusDto.Outdated, checkedAt: Now.AddDays(-200));

        IRenderedComponent<ThirdPartyChecksPage> page = Open();

        page.Find("h1").TextContent.Should().Be("Vérifier les tiers");
        page.Find("#checks-simulated").TextContent.Should().Contain("simulés");
        page.Find("#checks-to-review").TextContent.Should().Be("1");
        page.Find("#checks-to-do").TextContent.Should().Be("2");
        page.Find("#checks-clear").TextContent.Should().Be("1");
        page.FindAll("#checks-review-hint").Should().ContainSingle();
        Names(page).Should().Equal("Import Lumière", "Atelier Zola", "Zinc et fils", "Boulangerie Martin");
        string flagged = page.Find("#checks-table tbody tr").TextContent;
        flagged.Should().Contain("Ressemble à « Viktor Fictivenko »").And.Contain("Procédure collective, publiée le 15/09/2026")
            .And.Contain("Code non reconnu").And.Contain("1 inscrit(s)").And.Contain("Fournisseur · 542051180");
        page.Find("#checks-table tbody tr td a").GetAttribute("href").Should().StartWith("suppliers/");

        page.Find("#checks-filter-status").Change("to-handle");
        Names(page).Should().Equal("Import Lumière", "Atelier Zola", "Zinc et fils");
        page.Find("#checks-filter-status").Change("Clear");
        Names(page).Should().Equal("Boulangerie Martin");
        page.Find("#checks-filter-status").Change(string.Empty);
        page.Find("#checks-filter-kind").Change("Supplier");
        Names(page).Should().Equal("Import Lumière", "Zinc et fils");
        page.Find("#checks-filter-kind").Change(string.Empty);
        page.Find("#checks-search").Input("542051");
        Names(page).Should().Equal("Import Lumière");
        page.Find("#checks-search").Input("aucun");
        page.FindAll("#checks-filtered-empty").Should().ContainSingle();
        page.Find("#checks-filters-reset").Click();
        page.Find("#checks-sort-party").Click();
        Names(page).Should().Equal("Atelier Zola", "Boulangerie Martin", "Import Lumière", "Zinc et fils");
    }

    [Fact]
    public void Verifier_un_tiers_met_sa_ligne_a_jour_et_le_dit()
    {
        ThirdPartyCheckRow zola = _checks.Add("Atelier Zola", siren: null);
        ThirdPartyCheckRow martin = _checks.Add("Boulangerie Martin", ThirdPartyKindDto.Supplier);
        _checks.Answers[martin.PartyId] = r => r with
        {
            Status = ThirdPartyCheckStatusDto.ToReview,
            Notice = LegalNoticeKindDto.StruckOff,
            NoticeOn = new DateOnly(2026, 8, 1),
            NoticeLabel = "Radiation",
        };
        IRenderedComponent<ThirdPartyChecksPage> page = Open();

        page.Find($"#check-run-{zola.PartyId}").TextContent.Trim().Should().Be("Vérifier");
        page.Find($"#check-run-{zola.PartyId}").Click();
        this.ToastText().Should().Be("Atelier Zola vérifié : Rien à signaler.");
        string row = page.Find($"#check-{zola.PartyId}").TextContent;
        row.Should().Contain("Pas de SIREN").And.Contain("Aucun nom ressemblant").And.Contain("Aucune annonce").And.Contain("28/10/2026").And.NotContain("Rien à vérifier");
        page.Find($"#check-run-{zola.PartyId}").TextContent.Trim().Should().Be("Revérifier");

        page.Find($"#check-run-{martin.PartyId}").Click();
        this.Toasts()[^1].Kind.Should().Be(ToastKind.Warning);
        page.Find($"#check-{martin.PartyId}").TextContent.Should().Contain("Radiation, publiée le 01/08/2026").And.Contain("2 inscrit(s)").And.Contain("Rien à vérifier");
        page.Find("#checks-to-review").TextContent.Should().Be("1");
    }

    [Fact]
    public void Tout_verifier_passe_les_tiers_un_a_un_et_s_arrete_au_premier_refus()
    {
        ThirdPartyCheckRow a = _checks.Add("Atelier Zola");
        _checks.Add("Boulangerie Martin");
        ThirdPartyCheckRow c = _checks.Add("Café du Port");
        _checks.Add("Déjà vu", status: ThirdPartyCheckStatusDto.Clear, checkedAt: Now.AddDays(-2));
        _checks.Answers[c.PartyId] = r => r with { Status = ThirdPartyCheckStatusDto.ToReview, Freeze = FreezeScreeningDto.PossibleMatch, FreezeMatch = "Boris Inventov" };
        IRenderedComponent<ThirdPartyChecksPage> page = Open();

        page.Find("#checks-check-all").TextContent.Should().Contain("Vérifier les 3 tiers");
        page.Find("#checks-check-all").Click();

        page.WaitForAssertion(() => _checks.Checked.Should().HaveCount(3));
        this.ToastText().Should().Be("3 tiers vérifiés, dont 1 à examiner.");
        page.FindAll("#checks-check-all").Should().BeEmpty("plus rien à vérifier");
        page.FindAll("#checks-progress").Should().BeEmpty();
        page.Find($"#check-{c.PartyId}").TextContent.Should().Contain("Boris Inventov");

        _checks.Rows[0] = _checks.Rows[0] with { Status = ThirdPartyCheckStatusDto.Outdated };
        _checks.Rows[1] = _checks.Rows[1] with { Status = ThirdPartyCheckStatusDto.Outdated };
        IRenderedComponent<ThirdPartyChecksPage> again = Open();
        _checks.FailOn = (5, ErrorCodes.Directory.Unavailable);
        again.Find("#checks-check-all").Click();
        again.WaitForAssertion(() => this.ToastText().Should().StartWith("Vérification arrêtée après 1 tiers"));
        _checks.Checked.Should().HaveCount(5);
        _checks.Checked[3].Should().Be(a.PartyId);
    }

    [Fact]
    public void Un_tiers_disparu_recharge_la_liste_et_un_refus_de_lecture_s_affiche()
    {
        ThirdPartyCheckRow gone = _checks.Add("Atelier Zola");
        IRenderedComponent<ThirdPartyChecksPage> page = Open();
        _checks.FailOn = (1, ErrorCodes.ThirdPartyCheck.PartyNotFound);
        _checks.Rows.Clear();

        page.Find($"#check-run-{gone.PartyId}").Click();

        this.ToastText().Should().Contain("n'existe plus");
        page.WaitForAssertion(() => page.FindAll("#checks-empty").Should().ContainSingle());

        _checks.NextListErrors = [new ErrorDetail(ErrorCodes.Api.Unreachable)];
        IRenderedComponent<ThirdPartyChecksPage> failed = Open();
        failed.FindAll("#checks-empty").Should().ContainSingle();
    }
}
