using System.Globalization;
using Bunit;
using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Explorer;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Parties;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests;

[Collection("culture")]
public sealed class PayeeChecksPageTests : BunitContext
{
    private readonly FakeIbanChangesGateway _changes = new();
    private readonly FakeSuppliersGateway _suppliers = new();

    public PayeeChecksPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IIbanChangesGateway>(_changes);
        Services.AddSingleton<ISuppliersGateway>(_suppliers);
        UseCulture("fr-FR");
    }

    private static void UseCulture(string name) => LettraCultures.Apply(CultureInfo.GetCultureInfo(name));

    private IRenderedComponent<PayeeChecksPage> Open()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("iban-changes/payees");
        IRenderedComponent<PayeeChecksPage> page = Render<PayeeChecksPage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    private PayeeCheckRowResponse Add(string name, PayeeMatchDto? holder = null, PayeeCheckResponse? check = null, bool ibanChanged = false)
    {
        SupplierResponse supplier = _suppliers.Add(name, "Lyon", "FR7630006000011234567890189");
        PayeeCheckRowResponse row = new(supplier.Id, name, holder is null ? null : "Schmidt Handels GmbH", "FR76 •••• 0189", holder, check, ibanChanged,
            ibanChanged || holder == PayeeMatchDto.NoMatch || check?.Result != PayeeMatchDto.Match);
        _changes.Payees.Add(row);
        return row;
    }

    [Fact]
    public void Chaque_beneficiaire_montre_ses_controles_avant_paiement()
    {
        PayeeCheckRowResponse strange = Add("Hébergeur Les Pins", holder: PayeeMatchDto.NoMatch);
        PayeeCheckRowResponse close = Add("Minoterie Dupuis", check: new(PayeeMatchDto.CloseMatch, new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero), "Minoterie Dupuy"));
        PayeeCheckRowResponse changed = Add("Location de salles", ibanChanged: true);
        Add("Assurance de la Vallée", check: new(PayeeMatchDto.Match, DateTimeOffset.UnixEpoch, null));

        IRenderedComponent<PayeeChecksPage> page = Open();

        page.Find("h1").TextContent.Should().Be("Nom et IBAN des bénéficiaires");
        page.Find("#tab-payees").ClassList.Should().Contain("active");
        page.FindAll("#payees-table tbody tr").Should().HaveCount(3, "seuls ceux à contrôler, par défaut");
        page.Find($"tr[data-supplier='{strange.SupplierId}']").TextContent.Should().Contain("Schmidt Handels GmbH").And.Contain("Ne ressemble pas au nom du fournisseur").And.Contain("Jamais vérifié");
        page.Find($"tr[data-supplier='{close.SupplierId}']").TextContent.Should().Contain("Correspond presque").And.Contain("Titulaire : Minoterie Dupuy").And.Contain("le nom du fournisseur");
        page.Find($"tr[data-supplier='{changed.SupplierId}']").TextContent.Should().Contain("IBAN modifié, à vérifier");
        _changes.PayeeQueries.Should().ContainSingle().Which.Should().Be(new PayeeCheckListQuery());
    }

    [Fact]
    public void Un_sous_traitant_sans_attestation_de_vigilance_renvoie_a_l_ecran_des_attestations()
    {
        PayeeCheckRowResponse due = Add("Maçonnerie Roux", check: new(PayeeMatchDto.Match, DateTimeOffset.UnixEpoch, null)) with { ToReview = true, AttestationDue = true };
        _changes.Payees[0] = due;
        PayeeCheckRowResponse close = Add("Minoterie Dupuis", check: new(PayeeMatchDto.CloseMatch, DateTimeOffset.UnixEpoch, "Minoterie Dupuy"));

        IRenderedComponent<PayeeChecksPage> page = Open();

        page.Find($"#payee-attestation-{due.SupplierId}").TextContent.Should().Be("Attestation de vigilance à obtenir");
        page.Find($"#payee-attestation-{due.SupplierId}").GetAttribute("href").Should().Be("attestations");
        page.FindAll($"#payee-attestation-{close.SupplierId}").Should().BeEmpty();
    }

    [Fact]
    public void Verifier_un_beneficiaire_annonce_la_reponse_et_relit_la_liste()
    {
        PayeeCheckRowResponse row = Add("Hébergeur Les Pins");
        _suppliers.PayeeAnswer = new PayeeCheckResponse(PayeeMatchDto.NoMatch, DateTimeOffset.UnixEpoch, null);
        IRenderedComponent<PayeeChecksPage> page = Open();

        page.Find($"#payee-verify-{row.SupplierId}").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Hébergeur Les Pins : Ne correspond pas."));
        _suppliers.PayeeVerifications.Should().Equal(row.SupplierId);
        _changes.PayeeQueries.Should().HaveCount(2);
    }

    [Fact]
    public void Un_refus_de_la_verification_s_affiche_dans_un_toast()
    {
        PayeeCheckRowResponse row = Add("Hébergeur Les Pins");
        _suppliers.NextErrors = [new ErrorDetail(ErrorCodes.Supplier.IbanMissing, null)];
        IRenderedComponent<PayeeChecksPage> page = Open();

        page.Find($"#payee-verify-{row.SupplierId}").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Ce fournisseur n'a pas d'IBAN : rien à vérifier."));
    }

    [Fact]
    public void Sans_beneficiaire_a_controler_on_peut_tous_les_voir_puis_revenir_au_filtre()
    {
        Add("Assurance de la Vallée", check: new(PayeeMatchDto.Match, DateTimeOffset.UnixEpoch, null));
        IRenderedComponent<PayeeChecksPage> page = Open();
        page.Find("#payees-empty").TextContent.Should().Contain("Aucun bénéficiaire à contrôler");

        page.Find("#payees-show-all").Click();
        page.WaitForAssertion(() => page.FindAll("#payees-table tbody tr").Should().ContainSingle());

        page.Find("#payees-search").Change("zzz");
        page.WaitForAssertion(() => page.FindAll("#payees-filtered-empty").Should().ContainSingle());
        page.Find("#payees-empty-reset").Click();
        _changes.PayeeQueries[^1].Should().Be(new PayeeCheckListQuery());

        page.Find("#payees-show-all").Click();
        page.Find("#payees-sort-name").Click();
        _changes.PayeeQueries[^1].Sort.Should().Be(PayeeCheckSortDto.Name);

        UseCulture("en-US");
        Open().Find("h1").TextContent.Should().Be("Payee names and IBANs");
    }
}

/// <summary>La fiche fournisseur : titulaire du compte, contrôle local et vérification du bénéficiaire.</summary>
[Collection("culture")]
public sealed class SupplierPayeeTests : BunitContext
{
    private readonly FakeSuppliersGateway _suppliers = new();

    public SupplierPayeeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new ExplorerSettings { SearchDelay = TimeSpan.FromMilliseconds(1) });
        Services.AddLettraUi();
        Services.AddSingleton<ISuppliersGateway>(_suppliers);
        Services.AddSingleton<IDirectoryGateway>(new FakeDirectoryGateway());
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<SuppliersPage> Open(string route)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(route);
        IRenderedComponent<SuppliersPage> page = Render<SuppliersPage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    [Fact]
    public void La_verification_du_beneficiaire_se_lance_de_la_fiche_et_s_y_affiche()
    {
        SupplierResponse supplier = _suppliers.Add("Minoterie Dupuis", "Orléans", "FR7630006000011234567890189");
        _suppliers.Records[supplier.Id] = supplier with { AccountHolderName = "Jean Dupuis", HolderNameMatch = PayeeMatchDto.NoMatch };
        _suppliers.PayeeAnswer = new PayeeCheckResponse(PayeeMatchDto.CloseMatch, new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero), "Jean Dupuy");
        IRenderedComponent<SuppliersPage> page = Open($"suppliers/{supplier.Id}");
        page.Find("#supplier-account-holder").TextContent.Should().Be("Jean Dupuis");
        page.Find("#supplier-holder-match").TextContent.Trim().Should().Be("Ne ressemble pas au nom du fournisseur");
        page.Find("#supplier-payee-check").TextContent.Should().Contain("Jamais vérifié");

        page.Find("#supplier-payee-verify").Click();

        page.WaitForAssertion(() => page.Find("#supplier-payee-check").TextContent.Should().Contain("Correspond presque").And.Contain("Titulaire : Jean Dupuy"));
        this.ToastText().Should().Be("Minoterie Dupuis : Correspond presque.");
        _suppliers.PayeeVerifications.Should().Equal(supplier.Id);
    }

    [Fact]
    public void Un_refus_de_la_banque_s_affiche_dans_un_toast()
    {
        SupplierResponse supplier = _suppliers.Add("Minoterie Dupuis", "Orléans", "FR7630006000011234567890189");
        _suppliers.NextErrors = [new ErrorDetail(ErrorCodes.Directory.Unavailable, null)];
        IRenderedComponent<SuppliersPage> page = Open($"suppliers/{supplier.Id}");

        page.Find("#supplier-payee-verify").Click();

        page.WaitForAssertion(() => this.ToastText().Should().NotBeNullOrEmpty());
        page.Find("#supplier-payee-check").TextContent.Should().Contain("Jamais vérifié");
    }

    [Fact]
    public void Sans_iban_la_fiche_ne_propose_pas_de_verification_et_le_titulaire_se_saisit()
    {
        SupplierResponse supplier = _suppliers.Add("Minoterie Dupuis", "Orléans");
        IRenderedComponent<SuppliersPage> page = Open($"suppliers/{supplier.Id}");
        page.FindAll("#supplier-payee-verify").Should().BeEmpty();

        page = Open($"suppliers/{supplier.Id}/edit");
        page.Find("#account-holder").GetAttribute("placeholder").Should().Be("Minoterie Dupuis");
        page.Find("#iban").Change("FR7630006000011234567890189");
        page.Find("#account-holder").Change("Jean Dupuis");
        page.Find("form").Submit();

        page.WaitForAssertion(() => _suppliers.Updates.Should().ContainSingle().Which.Request.AccountHolderName.Should().Be("Jean Dupuis"));
    }
}
