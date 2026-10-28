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

namespace Lettra.UI.Tests.Attestations;

/// <summary>« Attestations de vigilance » : les fournisseurs à suivre, ceux à ne pas payer en l'état, et la saisie d'une attestation.</summary>
[Collection("culture")]
public sealed class AttestationsPageTests : BunitContext
{
    private static readonly DateOnly Today = new(2026, 10, 26);
    private readonly FakeAttestationsGateway _attestations = new(Today);

    public AttestationsPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IAttestationsGateway>(_attestations);
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<AttestationsPage> Open()
    {
        IRenderedComponent<AttestationsPage> page = Render<AttestationsPage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    private static string[] Names(IRenderedComponent<AttestationsPage> page) =>
        [.. page.FindAll("#attestations-table tbody tr td:first-child").Select(c => c.TextContent.Trim())];

    [Fact]
    public void Ce_qui_bloque_un_paiement_vient_d_abord_et_se_filtre()
    {
        _attestations.Add("Atelier Brun");
        _attestations.Add("Zinc et fils", 9_000m, new DateOnly(2026, 9, 1), verified: true);
        _attestations.Add("Enduits Blanc", 6_000m, new DateOnly(2026, 5, 10), verified: true);
        _attestations.Add("Béton Martin", 18_000m);
        _attestations.Add("Charpente Roux", 12_000m, new DateOnly(2026, 3, 1), verified: true);
        _attestations.Add("Dallage Petit", 7_500m, new DateOnly(2026, 10, 1));

        IRenderedComponent<AttestationsPage> page = Open();

        page.Find("h1").TextContent.Should().Be("Attestations de vigilance des fournisseurs");
        page.Find("#attestations-blocking").TextContent.Should().Be("3");
        page.Find("#attestations-soon").TextContent.Should().Be("1");
        page.Find("#attestations-valid").TextContent.Should().Be("1");
        page.Find("#attestations-not-required").TextContent.Should().Be("1");
        Names(page).Should().Equal("Charpente Roux", "Béton Martin", "Dallage Petit", "Enduits Blanc", "Zinc et fils", "Atelier Brun");
        page.Find("#attestations-table tbody tr").TextContent.Should().Contain("Expirée").And.Contain("31/08/2026").And.Contain("du 01/03/2026");

        page.Find("#attestations-filter-status").Change("to-handle");
        Names(page).Should().Equal("Charpente Roux", "Béton Martin", "Dallage Petit");
        page.Find("#attestations-filter-status").Change("Valid");
        Names(page).Should().Equal("Zinc et fils");
        page.Find("#attestations-filter-status").Change(string.Empty);
        page.Find("#attestations-search").Input("beton");
        Names(page).Should().Equal("Béton Martin");
        page.Find("#attestations-search").Input("aucun");
        page.FindAll("#attestations-filtered-empty").Should().ContainSingle();
        page.Find("#attestations-search").Input(string.Empty);
        page.Find("#attestations-sort-supplier").Click();
        Names(page).Should().Equal("Atelier Brun", "Béton Martin", "Charpente Roux", "Dallage Petit", "Enduits Blanc", "Zinc et fils");
    }

    [Fact]
    public void Suivre_un_fournisseur_puis_noter_l_attestation_verifiee()
    {
        SupplierAttestationRow roux = _attestations.Add("Maçonnerie Roux");
        IRenderedComponent<AttestationsPage> page = Open();

        page.Find($"#attestation-edit-{roux.SupplierId}").TextContent.Trim().Should().Be("Suivre");
        page.Find($"#attestation-edit-{roux.SupplierId}").Click();
        page.Find("#attestation-form-title").TextContent.Should().Be("Attestation de Maçonnerie Roux");
        page.Find("#attestation-amount").Change("18000");
        page.Find("#attestation-issued").Change("15/10/2026");
        page.Find("#attestation-code").Change("ab12 cd34 ef56");
        page.Find("#attestation-verified").Change("20/10/2026");
        page.Find("#attestation-form").Submit();

        page.WaitForAssertion(() => page.FindAll("#attestation-form").Should().BeEmpty());
        (Guid id, SaveAttestationRequest request, long? version) = _attestations.Saves.Should().ContainSingle().Subject;
        id.Should().Be(roux.SupplierId);
        version.Should().BeNull("le fournisseur n'était pas suivi");
        request.Should().Be(new SaveAttestationRequest
        {
            ContractAmountExclTax = 18_000m,
            IssuedOn = new DateOnly(2026, 10, 15),
            SecurityCode = "ab12 cd34 ef56",
            VerifiedOn = new DateOnly(2026, 10, 20),
        });
        this.ToastText().Should().Be("Maçonnerie Roux : Valable.");
        page.Find($"#attestation-{roux.SupplierId}").TextContent.Should().Contain("Valable").And.Contain("14/04/2027");
        page.Find($"#attestation-edit-{roux.SupplierId}").TextContent.Trim().Should().Be("Mettre à jour");

        page.Find($"#attestation-edit-{roux.SupplierId}").Click();
        page.Find("#attestation-code").GetAttribute("value").Should().Be("AB12CD34EF56");
        page.Find("#attestation-amount").GetAttribute("value").Should().Be("18000");
        page.Find("#attestation-cancel").Click();
        page.FindAll("#attestation-form").Should().BeEmpty();
    }

    [Fact]
    public void Les_oublis_previsibles_sont_signales_sans_appel_et_les_refus_du_serveur_s_affichent_au_champ()
    {
        SupplierAttestationRow roux = _attestations.Add("Maçonnerie Roux", 18_000m);
        IRenderedComponent<AttestationsPage> page = Open();
        page.Find($"#attestation-edit-{roux.SupplierId}").Click();

        page.Find("#attestation-verified").Change("20/10/2026");
        page.Find("#attestation-form").Submit();
        page.Find("#attestation-form").TextContent.Should().Contain("Pour noter une vérification, saisissez la date de l'attestation et son code.");
        page.FindAll("#attestation-not-saved").Should().ContainSingle();

        page.Find("#attestation-issued").Change("21/10/2026");
        page.Find("#attestation-code").Change("AB12CD34EF56");
        page.Find("#attestation-form").Submit();
        page.Find("#attestation-form").TextContent.Should().Contain("La vérification ne peut pas précéder la date de l'attestation.");

        page.Find("#attestation-amount").Change("-1");
        page.Find("#attestation-form").Submit();
        page.Find("#attestation-form").TextContent.Should().Contain("Le montant du contrat est positif, au centime près.");
        _attestations.Saves.Should().BeEmpty("rien ne part au serveur");

        page.Find("#attestation-amount").Change("18000");
        page.Find("#attestation-verified").Change("22/10/2026");
        page.Find("#attestation-code").Change("ABC");
        _attestations.NextErrors = [new ErrorDetail(ErrorCodes.Attestation.CodeInvalid, "SecurityCode")];
        page.Find("#attestation-form").Submit();
        page.WaitForAssertion(() => page.Find("#attestation-form").TextContent.Should().Contain("Le code de sécurité compte de 8 à 20 lettres et chiffres."));
        page.FindAll("#attestation-not-saved").Should().ContainSingle();
        this.ToastText().Should().BeEmpty();
    }

    [Fact]
    public void Sans_fournisseur_l_ecran_l_explique_et_parle_anglais()
    {
        LettraCultures.Apply(CultureInfo.GetCultureInfo("en-GB"));
        IRenderedComponent<AttestationsPage> page = Open();

        page.FindAll("#attestations-empty").Should().ContainSingle();
        page.Find("h1").TextContent.Should().Be("Suppliers' due diligence certificates");
    }

    [Fact]
    public void Un_refus_general_s_affiche_en_haut()
    {
        _attestations.NextErrors = [new ErrorDetail(ErrorCodes.Security.Forbidden)];
        IRenderedComponent<AttestationsPage> page = Open();

        page.FindAll("#attestations-empty").Should().ContainSingle();
        this.ToastText().Should().NotBeEmpty();
    }

    [Fact]
    public void Le_code_d_une_attestation_se_fait_verifier_par_l_urssaf()
    {
        SupplierAttestationRow dallage = _attestations.Add("Dallage Petit", 7_500m, new DateOnly(2026, 10, 1));
        SupplierAttestationRow brun = _attestations.Add("Atelier Brun");
        IRenderedComponent<AttestationsPage> page = Open();

        page.FindAll($"#attestation-verify-{brun.SupplierId}").Should().BeEmpty("sans code, rien à vérifier");
        page.Find($"#attestation-verify-{dallage.SupplierId}").Click();

        _attestations.Verifications.Should().Equal(dallage.SupplierId);
        this.ToastText().Should().Be("Code reconnu par l'URSSAF : vérification notée pour Dallage Petit.");
        page.Find($"#attestation-{dallage.SupplierId}").TextContent.Should().Contain("26/10/2026").And.Contain("Valable");

        _attestations.Urssaf = UrssafAnswerDto.NotRecognized;
        page.Find($"#attestation-verify-{dallage.SupplierId}").Click();
        this.Toasts()[^1].Kind.Should().Be(ToastKind.Error);
        this.ToastText().Should().Contain("ne reconnaît pas ce code");

        _attestations.NextErrors = [new ErrorDetail(ErrorCodes.Attestation.SirenMissing)];
        page.Find($"#attestation-verify-{dallage.SupplierId}").Click();
        this.ToastText().Should().Contain("SIREN");
    }
}
