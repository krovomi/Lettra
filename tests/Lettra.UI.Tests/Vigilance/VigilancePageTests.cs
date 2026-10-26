using System.Globalization;
using Bunit;
using Lettra.Contracts.Common;
using Lettra.Contracts.Vigilance;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Vigilance;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Vigilance;

/// <summary>« Anti-blanchiment » : la fiche de connaissance du client, son risque et sa revue.</summary>
[Collection("culture")]
public sealed class VigilancePageTests : BunitContext
{
    private readonly FakeVigilanceGateway _vigilance = new();

    public VigilancePageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IVigilanceGateway>(_vigilance);
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<VigilancePage> Open()
    {
        IRenderedComponent<VigilancePage> page = Render<VigilancePage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    [Fact]
    public void Sans_fiche_le_formulaire_s_ouvre_et_l_enregistrement_calcule_la_prochaine_revue()
    {
        IRenderedComponent<VigilancePage> page = Open();

        page.Find("#vigilance-status").TextContent.Should().Be("À faire");
        page.Find("#vigilance-evidence-CompanyRegistration").Change(true);
        page.Find("#vigilance-evidence-ManagerIdentity").Change(true);
        page.Find("#vigilance-verified").Change("01/10/2026");
        page.Find("#owner-name-0").Change("Jeanne Martin");
        page.Find("#owner-share-0").Change("75");
        page.Find("#vigilance-owner-add").Click();
        page.Find("#owner-name-1").Change("Paul Martin");
        page.Find("#owner-kind-1").Change("LegalRepresentative");
        page.Find("#owner-share-1").HasAttribute("disabled").Should().BeTrue("un représentant légal n'a pas de part");
        page.Find("#vigilance-form").Submit();

        page.WaitForAssertion(() => page.FindAll("#vigilance-summary").Should().ContainSingle());
        (SaveDueDiligenceRequest request, long? version) = _vigilance.Saves.Should().ContainSingle().Subject;
        version.Should().BeNull();
        request.Evidence.Should().Equal(IdentityEvidenceDto.CompanyRegistration, IdentityEvidenceDto.ManagerIdentity);
        request.IdentityVerifiedOn.Should().Be(new DateOnly(2026, 10, 1));
        request.ReviewedOn.Should().Be(new DateOnly(2026, 10, 26), "la revue est datée du jour");
        request.Level.Should().Be(RiskLevelDto.Standard);
        request.Owners.Should().Equal(
            new BeneficialOwnerDto { Name = "Jeanne Martin", SharePercent = 75m, Kind = OwnershipKindDto.Capital },
            new BeneficialOwnerDto { Name = "Paul Martin", Kind = OwnershipKindDto.LegalRepresentative });
        this.ToastText().Should().Be("Fiche enregistrée. Prochaine revue calculée selon le risque.");
        page.Find("#vigilance-status").TextContent.Should().Be("À jour");
        page.Find("#vigilance-next").TextContent.Should().Be("26/10/2028");
        page.Find("#vigilance-owner-list").TextContent.Should().Contain("75 % du capital").And.Contain("Représentant légal");
    }

    [Fact]
    public void Revoir_la_fiche_repart_de_la_version_lue_et_date_la_revue_du_jour()
    {
        _vigilance.File = FakeVigilanceGateway.Sample();
        IRenderedComponent<VigilancePage> page = Open();

        page.Find("#vigilance-level").TextContent.Should().Be("Élevé");
        page.Find("#vigilance-next").TextContent.Should().Be("02/03/2027");
        page.Find("#vigilance-stats").TextContent.Should().Contain("Revue le 02/03/2026 par Paul Leroy");
        page.Find("#vigilance-summary").TextContent.Should().Contain("Personne politiquement exposée").And.Contain("Élue municipale.");
        page.Find("#vigilance-edit").Click();
        page.Find("#vigilance-factor-PoliticallyExposed").HasAttribute("checked").Should().BeTrue();
        page.Find("#vigilance-level-select").Change("Standard");
        page.FindAll("#vigilance-high-hint").Should().ContainSingle("une personne politiquement exposée impose le niveau élevé");
        page.Find("#vigilance-level-select").Change("High");
        page.FindAll("#vigilance-high-hint").Should().BeEmpty();
        page.Find("#owner-remove-1").Click();
        page.Find("#vigilance-form").Submit();

        page.WaitForAssertion(() => _vigilance.Saves.Should().ContainSingle());
        (SaveDueDiligenceRequest request, long? version) = _vigilance.Saves[0];
        version.Should().Be(4);
        request.ReviewedOn.Should().Be(new DateOnly(2026, 10, 26));
        request.Owners.Should().ContainSingle().Which.Name.Should().Be("Jeanne Martin");
        request.Factors.Should().Equal(RiskFactorDto.PoliticallyExposed);
        request.Justification.Should().Be("Élue municipale.");
    }

    [Fact]
    public void Une_relation_terminee_montre_jusqu_a_quand_garder_la_fiche()
    {
        _vigilance.File = FakeVigilanceGateway.Sample(RiskLevelDto.Standard, new DateOnly(2026, 6, 30));
        IRenderedComponent<VigilancePage> page = Open();

        page.Find("#vigilance-status").TextContent.Should().Be("Relation terminée");
        page.Find("#vigilance-keep").TextContent.Should().Be("À garder jusqu'au 30/06/2031");
        page.Find("#vigilance-next").TextContent.Should().Be("—");
    }

    [Fact]
    public void Un_oubli_previsible_est_signale_sans_appeler_le_serveur()
    {
        IRenderedComponent<VigilancePage> page = Open();

        page.Find("#owner-name-0").Change("Jeanne Martin");
        page.Find("#owner-share-0").Change("20");
        page.Find("#vigilance-factor-HighRiskCountry").Change(true);
        page.Find("#vigilance-form").Submit();

        _vigilance.Saves.Should().BeEmpty("rien ne part au serveur");
        this.Toasts().Should().BeEmpty();
        page.Find("#vigilance-not-saved").TextContent.Should().Be("La fiche n'est pas enregistrée : corrigez les champs signalés.");
        page.Find("#vigilance-form").TextContent.Should().Contain("Une part de capital ou de droits de vote dépasse 25 %");
        page.FindAll(".validation-message").Select(e => e.TextContent).Should().Contain("Une personne politiquement exposée ou un pays à risque impose le niveau élevé.");
        page.FindAll("#vigilance-high-hint").Should().BeEmpty("l'erreur du champ remplace le rappel");

        page.Find("#owner-share-0").Change("100");
        page.Find("#vigilance-level-select").Change("High");
        page.Find("#vigilance-justification").Change("Filiale au Panama.");
        page.Find("#vigilance-form").Submit();

        page.WaitForAssertion(() => _vigilance.Saves.Should().ContainSingle());
        page.FindAll("#vigilance-not-saved").Should().BeEmpty();
    }

    [Fact]
    public void Un_refus_de_champ_du_serveur_s_affiche_sous_le_champ()
    {
        IRenderedComponent<VigilancePage> page = Open();
        _vigilance.NextErrors = [new ErrorDetail(ErrorCodes.Vigilance.DateInFuture, "ReviewedOn")];

        page.Find("#owner-name-0").Change("Jeanne Martin");
        page.Find("#owner-share-0").Change("60");
        page.Find("#vigilance-form").Submit();

        page.WaitForAssertion(() => page.FindAll("#vigilance-not-saved").Should().ContainSingle());
        page.Find("#vigilance-form").TextContent.Should().Contain("Une date de vérification, de revue ou de fin ne peut pas être dans le futur.");
        page.FindAll("#vigilance-form").Should().ContainSingle("le formulaire reste ouvert");
    }

    [Fact]
    public void Un_refus_de_droit_s_affiche_une_seule_fois()
    {
        _vigilance.File = FakeVigilanceGateway.Sample();
        IRenderedComponent<VigilancePage> page = Open();
        _vigilance.NextErrors = [new ErrorDetail(ErrorCodes.Security.Forbidden)];

        page.Find("#vigilance-edit").Click();
        page.Find("#vigilance-form").Submit();

        page.WaitForAssertion(() => this.Toasts().Should().ContainSingle().Which.Kind.Should().Be(ToastKind.Error));
        _vigilance.Saves.Should().BeEmpty();
    }
}
