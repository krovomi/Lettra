using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Palette;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Parties;
using Lettra.UI.Shared.Typing;
using Lettra.UI.Tests.Billing;
using Lettra.UI.Tests.Cabinet;
using Lettra.UI.Tests.Consultation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Typing;

/// <summary>Champ assisté (vague 5, lane F) : autocomplétion au clavier, modèles par « / » et par la palette, nettoyage.</summary>
[Collection("culture")]
public sealed class SmartInputTests : BunitContext
{
    private static readonly Suggestion[] Villes =
    [
        new("69001", "Lyon 1er Arrondissement", "69001"),
        new("69002", "Lyon 2e Arrondissement", "69002"),
        new("69003", "Lyon 3e Arrondissement", "69003"),
    ];

    private readonly BunitJSModuleInterop _assist;

    public SmartInputTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _assist = JSInterop.SetupModule(AssistModule.Path);
        Services.AddLettraUi();
        Services.AddCabinetFakes();
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    [Fact]
    public void Les_suggestions_se_parcourent_au_clavier_selon_le_motif_combobox()
    {
        Suggestion? choisie = null;
        IRenderedComponent<SmartInput> champ = Render<SmartInput>(p => p
            .Add(c => c.Id, "ville")
            .Add(c => c.Suggest, _ => Task.FromResult<IReadOnlyList<Suggestion>>(Villes))
            .Add(c => c.OnPicked, s => choisie = s));
        IElement input() => champ.Find("#ville");

        input().GetAttribute("role").Should().Be("combobox");
        input().GetAttribute("aria-expanded").Should().Be("false");

        input().Input("lyo");

        input().GetAttribute("aria-expanded").Should().Be("true");
        champ.FindAll("[role=option]").Select(o => o.TextContent.Trim()).Should().HaveCount(3);
        input().GetAttribute("aria-activedescendant").Should().Be("ville-option-0");
        champ.Find("[aria-live]").TextContent.Should().Be("3 suggestions, flèches pour parcourir, Entrée pour choisir.");

        input().KeyDown("ArrowDown");
        input().KeyDown("ArrowDown");
        input().KeyDown("ArrowDown");
        input().GetAttribute("aria-activedescendant").Should().Be("ville-option-0", "la liste reboucle");
        input().KeyDown("ArrowUp");
        champ.Find("#ville-option-2").GetAttribute("aria-selected").Should().Be("true");

        input().KeyDown("Enter");

        choisie.Should().Be(Villes[2]);
        champ.FindAll("[role=listbox]").Should().BeEmpty();
        _assist.Invocations.Should().Contain(i => i.Identifier == "guardCombobox", "Entrée ne doit pas envoyer le formulaire");
    }

    [Fact]
    public void Echap_ferme_la_liste_et_un_clic_choisit()
    {
        string? valeur = null;
        IRenderedComponent<SmartInput> champ = Render<SmartInput>(p => p
            .Add(c => c.Id, "ville")
            .Add(c => c.Suggest, _ => Task.FromResult<IReadOnlyList<Suggestion>>(Villes))
            .Add(c => c.ValueChanged, v => valeur = v));

        champ.Find("#ville").Input("lyo");
        champ.Find("#ville").KeyDown("Escape");
        champ.FindAll("[role=option]").Should().BeEmpty();

        champ.Find("#ville").Input("lyon");
        champ.Find("#ville-option-1").MouseDown();

        valeur.Should().Be("69002", "sans rappel OnPicked, la valeur de la proposition remplace le texte");
    }

    [Fact]
    public void La_barre_oblique_propose_les_modeles_du_champ_et_insere_le_texte()
    {
        string? enregistre = null;
        IRenderedComponent<SmartInput> champ = Render<SmartInput>(p => p
            .Add(c => c.Id, "payment-terms")
            .Add(c => c.Templates, TextFieldKind.PaymentTerms)
            .Add(c => c.OnCommitted, v => enregistre = v));

        champ.Find("#payment-terms-hint").TextContent.Should().Be("« / » pour insérer un modèle de texte.");
        champ.Find("#payment-terms").Input("Virement. /pena");

        champ.FindAll("[role=option]").Select(o => o.TextContent.Trim()).Should().Equal("Pénalités de retard");
        champ.Find("[role=listbox]").GetAttribute("aria-label").Should().Be("Modèles de texte");
        champ.Find("#payment-terms").KeyDown("Enter");

        enregistre.Should().StartWith("Virement. En cas de retard de paiement, des pénalités").And.EndWith("du Code de commerce).");
    }

    [Fact]
    public void Le_texte_libre_est_corrige_en_sortie_et_les_codes_gardent_leur_saisie()
    {
        string? libre = null, code = null;
        IRenderedComponent<SmartInput> texte = Render<SmartInput>(p => p.Add(c => c.Id, "subject").Add(c => c.Tidy, true).Add(c => c.Spellcheck, true)
            .Add(c => c.OnCommitted, v => libre = v));
        IRenderedComponent<SmartInput> numero = Render<SmartInput>(p => p.Add(c => c.Id, "siret").Add(c => c.OnCommitted, v => code = v));

        texte.Find("#subject").Change("  mission   annuelle ");
        numero.Find("#siret").Change(" 732 829 320 ");

        libre.Should().Be("Mission annuelle");
        code.Should().Be(" 732 829 320 ");
        texte.Find("#subject").GetAttribute("spellcheck").Should().Be("true");
        numero.Find("#siret").GetAttribute("spellcheck").Should().Be("false");
    }

    [Fact]
    public void Un_rendu_du_parent_pendant_la_frappe_n_efface_pas_le_texte()
    {
        IRenderedComponent<SmartInput> champ = Render<SmartInput>(p => p.Add(c => c.Id, "notes").Add(c => c.Value, "Avant"));

        champ.Find("#notes").Input("Avant, puis la suite");
        champ.Render(p => p.Add(c => c.Value, "Avant"));

        champ.Find("#notes").GetAttribute("value").Should().Be("Avant, puis la suite");
        champ.Render(p => p.Add(c => c.Value, "Rechargé"));
        champ.Find("#notes").GetAttribute("value").Should().Be("Rechargé", "une nouvelle valeur du parent l'emporte");
    }

    [Fact]
    public async Task La_palette_insere_un_modele_dans_le_dernier_champ_visite()
    {
        string? enregistre = null;
        IRenderedComponent<SmartInput> champ = Render<SmartInput>(p => p
            .Add(c => c.Id, "notes").Add(c => c.Multiline, true).Add(c => c.Value, "Première ligne")
            .Add(c => c.Templates, TextFieldKind.QuoteNotes).Add(c => c.OnCommitted, v => enregistre = v));
        TemplatePaletteProvider palette = Services.GetServices<ICommandPaletteProvider>().OfType<TemplatePaletteProvider>().Single();

        (await palette.SearchAsync("validite", CancellationToken.None)).Should().BeEmpty("aucun champ n'a encore eu le focus");
        champ.Find("#notes").FocusIn();
        IReadOnlyList<PaletteItem> items = await palette.SearchAsync("validite", CancellationToken.None);

        items.Should().ContainSingle().Which.Label.Should().Be("Insérer : Validité de l'offre");
        items[0].Group.Should().Be("Modèles de texte");
        await champ.InvokeAsync(items[0].Run);
        enregistre.Should().Be("Première ligne\nDevis valable 30 jours à compter de sa date d'émission.");
    }

    [Fact]
    public void Le_code_postal_remplit_la_ville_dans_la_fiche_client()
    {
        _assist.Setup<string?>("fetchText", PostalCodes.DataPath).SetResult("75009;Paris 9e Arrondissement\n69001;Lyon 1er Arrondissement\n");
        FakeCustomersGateway clients = new();
        Services.AddSingleton<ICustomersGateway>(clients);
        Services.AddSingleton<IDirectoryGateway>(new FakeDirectoryGateway());
        Services.AddSingleton<IAccountingGateway>(new FakeAccountingGateway());
        Services.AddSingleton<IInvoicesGateway>(new FakeInvoicesGateway());
        Services.AddSingleton<IQuotesGateway>(new FakeQuotesGateway());
        Services.GetRequiredService<NavigationManager>().NavigateTo("customers/new");
        IRenderedComponent<CustomersPage> page = Render<CustomersPage>();

        page.Find("#zip").Input("750");
        page.WaitForAssertion(() => page.FindAll("#zip-list [role=option]").Should().ContainSingle());
        page.Find("#zip-option-0").TextContent.Should().Contain("Paris 9e Arrondissement").And.Contain("75009");
        page.Find("#zip").KeyDown("Enter");

        page.Find("#zip").GetAttribute("value").Should().Be("75009");
        page.Find("#city").GetAttribute("value").Should().Be("Paris 9e Arrondissement");

        page.Find("#city").Input("lyon");
        page.WaitForAssertion(() => page.Find("#city-option-0").TextContent.Should().Contain("69001"));
    }
}
