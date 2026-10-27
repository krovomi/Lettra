using System.Globalization;
using Bunit;
using Lettra.Contracts.Common;
using Lettra.Contracts.Tax;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Tax;
using Lettra.UI.Tests.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Tax;

/// <summary>« Honoraires (DAS 2) » : les bénéficiaires suivis, le seuil de 2 400 €, la saisie, l'échéance à marquer déposée.</summary>
[Collection("culture")]
public sealed class FeeReturnPageTests : BunitContext
{
    private readonly FakeFeeReturnGateway _fees = new();
    private readonly FakeTaxGateway _tax = new();

    public FeeReturnPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IFeeReturnGateway>(_fees);
        Services.AddSingleton<ITaxGateway>(_tax);
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<FeeReturnPage> Open(string route = "tax/fees")
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(route);
        IRenderedComponent<FeeReturnPage> page = Render<FeeReturnPage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    private static string[] Names(IRenderedComponent<FeeReturnPage> page) =>
        [.. page.FindAll("#fees-table tbody tr td:first-child a").Select(a => a.TextContent.Trim())];

    [Fact]
    public void Les_beneficiaires_au_dela_de_2400_euros_sont_a_declarer_avec_l_echeance_de_l_exercice()
    {
        _fees.Add("Cabinet Durand", bank: 2_200m, outside: 300m);
        Guid roux = _fees.Add("Atelier Roux", bank: 800m, siret: null, nature: FeeNatureDto.Commissions);
        _fees.Add("Expert Martin", bank: 3_500m, siret: null);
        _fees.Add("Papeterie", tracked: false);

        IRenderedComponent<FeeReturnPage> page = Open();

        page.Find("h1").TextContent.Should().Be("Honoraires à déclarer (DAS 2)");
        page.Find("#tab-fees").ClassList.Should().Contain("active");
        page.Find("#fees-period").TextContent.Should().Contain("2025");
        page.Find("#fees-deadline").TextContent.Should().Contain("5 mai 2026").And.Contain("À venir");
        page.Find("#fees-to-declare").TextContent.Should().Be("2");
        page.Find("#fees-below").TextContent.Should().Be("1");
        page.Find("#fees-bank-until").TextContent.Should().Be("27/02/2026");
        page.FindAll("#fees-siret-missing").Should().ContainSingle("Expert Martin est à déclarer sans SIRET");
        Names(page).Should().Equal("Expert Martin", "Cabinet Durand", "Atelier Roux");
        page.Find($"#fee-{roux}").TextContent.Should().Contain("Commissions").And.Contain("Sous le seuil").And.Contain("SIRET manquant");
        _fees.Queries.Should().Equal([null]);

        page.Find("#fees-filter-state").Change("to-declare");
        Names(page).Should().Equal("Expert Martin", "Cabinet Durand");
        page.Find("#fees-filter-state").Change("siret-missing");
        Names(page).Should().Equal("Expert Martin", "Atelier Roux");
        page.Find("#fees-filter-state").Change("below");
        Names(page).Should().Equal("Atelier Roux");
        page.Find("#fees-filter-state").Change(string.Empty);
        page.Find("#fees-search").Input("avocat");
        Names(page).Should().HaveCount(3, "la profession compte aussi");
        page.Find("#fees-search").Input("durand");
        Names(page).Should().Equal("Cabinet Durand");
        page.Find("#fees-search").Input("aucun");
        page.FindAll("#fees-filtered-empty").Should().ContainSingle();
        page.Find("#fees-search").Input(string.Empty);
        page.Find("#fees-sort-name").Click();
        Names(page).Should().Equal("Atelier Roux", "Cabinet Durand", "Expert Martin");
        page.Find("#fees-sort-nature").Click();
        Names(page)[0].Should().Be("Atelier Roux", "Commissions vient avant Honoraires");
    }

    [Fact]
    public void Suivre_un_fournisseur_puis_noter_ce_qui_a_ete_paye_hors_releves()
    {
        Guid papeterie = _fees.Add("Papeterie", tracked: false);
        Guid lawyer = _fees.Add("Cabinet Durand", bank: 2_200m, tracked: false);
        IRenderedComponent<FeeReturnPage> page = Open();
        page.FindAll("#fees-empty").Should().ContainSingle();
        page.Find("#fees-deadline").TextContent.Should().Contain("Suivez un bénéficiaire");

        page.Find("#fees-add").Click();
        page.Find("#fee-supplier").Change(lawyer.ToString());
        page.Find("#fee-nature").Change("Fees");
        page.Find("#fee-profession").Change("Avocat");
        page.Find("#fee-outside").Change("250.5");
        page.Find("#fee-form").Submit();

        page.WaitForAssertion(() => page.FindAll("#fee-form").Should().BeEmpty());
        (Guid id, SaveFeeBeneficiaryRequest request, long? version) = _fees.Saves.Should().ContainSingle().Subject;
        (id, version).Should().Be((lawyer, null));
        request.Should().Be(new SaveFeeBeneficiaryRequest { Nature = FeeNatureDto.Fees, Profession = "Avocat", FiscalYearStart = FakeFeeReturnGateway.Start, PaidOutside = 250.5m });
        this.ToastText().Should().Be("Cabinet Durand : à déclarer.");
        page.Find($"#fee-{lawyer}").TextContent.Replace('\u202f', ' ').Replace('\u00a0', ' ').Should().Contain("2 450,50 €");

        page.Find($"#fee-edit-{lawyer}").Click();
        page.Find("#fee-form-title").TextContent.Should().Be("Honoraires de Cabinet Durand");
        page.FindAll("#fee-supplier").Should().BeEmpty("le fournisseur ne change pas");
        page.Find("#fee-outside").GetAttribute("value").Should().Be("250.5");
        page.Find("#fee-outside").Change("0");
        page.Find("#fee-form").Submit();
        page.WaitForAssertion(() => _fees.Saves.Should().HaveCount(2));
        _fees.Saves[1].Version.Should().Be(1);
        this.ToastText().Should().Be("Cabinet Durand : suivi, sous le seuil.");
        _fees.Saves.Should().NotContain(s => s.SupplierId == papeterie);
    }

    [Fact]
    public void Les_oublis_previsibles_sont_signales_sans_appel_et_les_refus_du_serveur_au_champ()
    {
        _fees.Add("Cabinet Durand", tracked: false);
        IRenderedComponent<FeeReturnPage> page = Open();
        page.Find("#fees-add").Click();

        page.Find("#fee-outside").Change("-3");
        page.Find("#fee-form").Submit();

        page.Find("#fee-form").TextContent.Should().Contain("Choisissez un fournisseur du dossier.")
            .And.Contain("Indiquez la profession du bénéficiaire.").And.Contain("Le montant est positif, au centime près.");
        page.FindAll("#fee-not-saved").Should().ContainSingle();
        _fees.Saves.Should().BeEmpty();

        page.Find("#fee-supplier").Change(page.FindAll("#fee-supplier option")[1].GetAttribute("value"));
        page.Find("#fee-profession").Change(new string('a', 60));
        page.Find("#fee-outside").Change("10");
        _fees.NextErrors = [new ErrorDetail(ErrorCodes.Fee.ProfessionTooLong, "Profession")];
        page.Find("#fee-form").Submit();
        page.WaitForAssertion(() => page.Find("#fee-form").TextContent.Should().Contain("La profession tient en 60 caractères."));
        this.ToastText().Should().BeEmpty();
        page.Find("#fee-cancel").Click();
        page.FindAll("#fee-form").Should().BeEmpty();
    }

    [Fact]
    public void Ne_plus_suivre_un_beneficiaire_et_marquer_la_declaration_deposee()
    {
        Guid lawyer = _fees.Add("Cabinet Durand", bank: 2_000m);
        IRenderedComponent<FeeReturnPage> page = Open("tax/fees?date=2025-06-30");
        _fees.Queries.Should().Equal(new DateOnly(2025, 6, 30));
        page.Find("#fees-previous").GetAttribute("href").Should().Be("tax/fees?date=2024-12-31");
        page.Find("#fees-next").GetAttribute("href").Should().Be("tax/fees?date=2026-01-01");

        page.Find("#fees-mark").Click();
        page.WaitForAssertion(() => _tax.Marks.Should().ContainSingle().Which.Should().Be(
            new MarkFiscalFilingRequest(FiscalObligationDto.FeeReturnDas2, FakeFeeReturnGateway.Start, true)));

        page.Find($"#fee-{lawyer} .lt-row-delete").Click();
        page.Find($"#fee-{lawyer} .lt-row-confirm-yes").Click();
        page.WaitForAssertion(() => _fees.Removals.Should().ContainSingle().Which.Should().Be((lawyer, FakeFeeReturnGateway.Start, 2L)));
        this.ToastText().Should().Be("Cabinet Durand n'est plus suivi.");
        page.FindAll("#fees-empty").Should().ContainSingle();
    }

    [Fact]
    public void Un_refus_general_reste_en_haut_et_l_ecran_parle_anglais()
    {
        _fees.NextErrors = [new ErrorDetail(ErrorCodes.Security.Forbidden)];
        IRenderedComponent<FeeReturnPage> page = Open();
        page.FindAll("#fees-table").Should().BeEmpty();
        this.ToastText().Should().NotBeEmpty();

        LettraCultures.Apply(CultureInfo.GetCultureInfo("en-GB"));
        _fees.Add("Durand Law", bank: 2_500m);
        IRenderedComponent<FeeReturnPage> english = Render<FeeReturnPage>();
        english.WaitForState(() => english.FindAll("#fees-table").Count > 0);
        english.Find("h1").TextContent.Should().Be("Fees to declare (DAS 2)");
        english.Find("#fees-table").TextContent.Should().Contain("To declare");
    }
}
