using System.Globalization;
using Bunit;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Speech;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Billing;
using Lettra.UI.Shared.Services;
using Lettra.UI.Shared.Typing;
using Lettra.UI.Shared.Voice;
using Lettra.UI.Tests.Billing;
using Lettra.UI.Tests.Cabinet;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Voice;

/// <summary>La voix dans l'interface (vague 5, lane F) : micro des champs, panneau, commandes, F8, voix désactivée.</summary>
[Collection("culture")]
public sealed class VoiceTests : BunitContext
{
    private readonly FakeSpeechGateway _speech = new();
    private readonly BunitJSModuleInterop _voice;
    private readonly BunitJSModuleInterop _ui;

    public VoiceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _voice = JSInterop.SetupModule(VoiceModule.Path);
        _ui = JSInterop.SetupModule(UiModule.Path);
        JSInterop.SetupModule(AssistModule.Path);
        _voice.Setup<bool>("supported").SetResult(true);
        _voice.Setup<bool>("secureContext").SetResult(true);
        _voice.Setup<string>("start", _ => true).SetResult("ok");
        _voice.Setup<byte[]?>("stop").SetResult(new byte[3200]);
        _voice.Setup<bool>("insertText", _ => true).SetResult(true);
        Services.AddLettraUi();
        Services.AddCabinetFakes();
        Services.AddSingleton<ISpeechGateway>(_speech);
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    /// <summary>Un champ assisté avec micro, le bouton de la barre du haut et l'hôte, comme dans la coquille.</summary>
    private IRenderedComponent<Coquille> Rendre() => Render<Coquille>();

    private sealed class Coquille : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<VoiceButton>(0);
            builder.CloseComponent();
            builder.OpenComponent<SmartInput>(1);
            builder.AddAttribute(2, nameof(SmartInput.Id), "subject");
            builder.AddAttribute(3, nameof(SmartInput.Voice), true);
            builder.AddAttribute(4, nameof(SmartInput.VoiceLabel), "Objet");
            builder.CloseComponent();
            builder.OpenComponent<VoiceHost>(5);
            builder.CloseComponent();
        }
    }

    [Fact]
    public void Sans_moteur_le_micro_du_haut_explique_et_les_champs_n_ont_pas_de_micro()
    {
        _speech.Status = SpeechStatusResponse.Disabled;
        IRenderedComponent<Coquille> page = Rendre();

        page.WaitForAssertion(() => page.Find("#voice-button").ClassList.Should().Contain("is-off"));
        page.FindAll("#subject-mic").Should().BeEmpty();
        page.Find("#voice-button").Click();

        page.Find("#voice-panel").GetAttribute("role").Should().Be("dialog");
        page.Find("#voice-state").TextContent.Should().StartWith("La dictée n'est pas activée sur ce déploiement.");
        page.Find("#voice-how-to-enable").TextContent.Should().Contain("Whisper");
        page.FindAll("#voice-speak").Should().BeEmpty();
        page.FindAll("#voice-examples li").Should().HaveCount(7, "ce que l'on pourra dire est montré quand même");
    }

    [Fact]
    public void Un_serveur_injoignable_le_dit_et_propose_de_reessayer()
    {
        _speech.StatusError = ErrorCodes.Api.Unreachable;
        IRenderedComponent<Coquille> page = Rendre();
        page.Find("#voice-button").Click();

        page.WaitForAssertion(() => page.Find("#voice-state").TextContent.Should().StartWith("Impossible de joindre le serveur"));
        _speech.StatusError = null;
        page.Find("#voice-retry").Click();

        page.WaitForAssertion(() => page.FindAll("#voice-speak").Should().ContainSingle());
    }

    [Fact]
    public void Un_moteur_qui_charge_son_modele_le_dit_puis_s_ouvre_tout_seul()
    {
        _speech.Status = _speech.Status with { Ready = false };
        Services.GetRequiredService<VoiceInput>().StartingRetryDelay = TimeSpan.FromMilliseconds(200);
        IRenderedComponent<Coquille> page = Rendre();
        page.Find("#voice-button").Click();

        page.WaitForAssertion(() => page.Find("#voice-state").TextContent.Should().StartWith("La dictée se prépare"));
        page.FindAll("#subject-mic").Should().BeEmpty("on ne laisse pas parler pour rien");
        page.FindAll("#voice-retry").Should().ContainSingle();
        _speech.Status = _speech.Status with { Ready = true };

        // Le panneau et le micro du champ se redessinent chacun de leur côté (InvokeAsync) : on attend les deux.
        page.WaitForAssertion(() => page.FindAll("#voice-speak").Should().ContainSingle(), TimeSpan.FromSeconds(5));
        page.WaitForAssertion(() => page.FindAll("#subject-mic").Should().ContainSingle(), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void En_http_sur_une_adresse_du_reseau_le_panneau_dit_d_utiliser_https_ou_localhost()
    {
        _voice.Setup<bool>("supported").SetResult(false);
        _voice.Setup<bool>("secureContext").SetResult(false);
        IRenderedComponent<Coquille> page = Rendre();
        page.Find("#voice-button").Click();

        page.WaitForAssertion(() => page.Find("#voice-state").TextContent.Should().Contain("https://").And.Contain("http://localhost:8080"));
        page.FindAll("#subject-mic").Should().BeEmpty();
    }

    [Fact]
    public void Le_micro_d_un_champ_dicte_et_insere_le_texte_au_curseur()
    {
        _speech.Text = "Mission annuelle, dossier 2026.";
        IRenderedComponent<Coquille> page = Rendre();

        page.WaitForAssertion(() => page.Find("#subject-mic").GetAttribute("aria-label").Should().Be("Dicter : Objet"));
        page.Find("#subject-mic").Click();
        page.Find("#subject-mic").GetAttribute("aria-pressed").Should().Be("true");
        page.Find("#voice-hud").TextContent.Should().Contain("Je vous écoute");
        page.Find("#voice-button").ClassList.Should().Contain("is-listening");

        page.Find("#subject-mic").Click();

        page.WaitForAssertion(() => page.Find("#voice-hud").TextContent.Should().Contain("Ajouté : « Mission annuelle, dossier 2026. »"));
        _speech.Calls.Should().ContainSingle().Which.Should().Be((SpeechMode.Dictation, "fr", 3200));
        _voice.Invocations.Should().Contain(i => i.Identifier == "insertText" && Equals(i.Arguments[0], "subject") && Equals(i.Arguments[1], "Mission annuelle, dossier 2026."));
        page.Find("#subject-mic").GetAttribute("aria-pressed").Should().Be("false");
    }

    [Fact]
    public void Un_micro_refuse_ou_rien_d_entendu_s_explique()
    {
        _voice.Setup<string>("start", _ => true).SetResult("denied");
        IRenderedComponent<Coquille> page = Rendre();
        page.WaitForAssertion(() => page.Find("#subject-mic"));
        page.Find("#voice-button").Click();

        page.Find("#voice-speak").Click();

        page.Find("#voice-result [role=alert]").TextContent.Should().StartWith("L'accès au micro a été refusé.");
    }

    [Fact]
    public void Le_choix_de_masquer_les_micros_est_retenu_sur_l_appareil()
    {
        IRenderedComponent<Coquille> page = Rendre();
        page.WaitForAssertion(() => page.Find("#subject-mic"));
        page.Find("#voice-button").Click();

        page.Find("#voice-fields").Change(false);

        page.FindAll("#subject-mic").Should().BeEmpty();
        page.Find("#voice-button").ClassList.Should().Contain("is-off");
        _ui.Invocations.Should().Contain(i => i.Identifier == "writePreference" && Equals(i.Arguments[0], VoiceInput.PreferenceKey) && Equals(i.Arguments[1], "off"));
    }

    [Fact]
    public void Une_commande_est_affichee_avant_d_etre_executee()
    {
        _speech.Text = "Ouvrir les factures en retard";
        _speech.Command = new VoiceCommand { Kind = VoiceCommandKind.Navigate, Phrase = "ouvrir les factures en retard", Target = VoiceTarget.Invoices, Filter = VoiceFilter.Overdue };
        IRenderedComponent<Coquille> page = Rendre();
        page.WaitForAssertion(() => page.Find("#voice-button").ClassList.Should().NotContain("is-off"));
        page.Find("#voice-button").Click();

        page.Find("#voice-speak").Click();
        page.Find("#voice-speak").GetAttribute("aria-pressed").Should().Be("true");
        page.Find("#voice-speak").Click();

        page.WaitForAssertion(() => page.Find("#voice-command").TextContent.Should().Be("Ouvrir : Factures (en retard)"));
        page.Find("#voice-heard").TextContent.Should().Be("Entendu : « Ouvrir les factures en retard »");
        _speech.Calls.Single().Mode.Should().Be(SpeechMode.Command);
        NavigationManager navigation = Services.GetRequiredService<NavigationManager>();
        navigation.Uri.Should().NotEndWith("invoices", "rien ne s'exécute sans clic");

        page.Find("#voice-run").Click();

        navigation.Uri.Should().EndWith("/invoices");
        page.FindAll("#voice-panel").Should().BeEmpty();
    }

    [Fact]
    public void Valider_ne_se_fait_jamais_a_la_voix()
    {
        _speech.Command = new VoiceCommand { Kind = VoiceCommandKind.Validate, Phrase = "valider", RequiresConfirmation = true };
        IRenderedComponent<Coquille> page = Rendre();
        page.WaitForAssertion(() => page.Find("#voice-button").ClassList.Should().NotContain("is-off"));
        page.Find("#voice-button").Click();
        page.Find("#voice-speak").Click();
        page.Find("#voice-speak").Click();

        page.WaitForAssertion(() => page.Find("#voice-run").Click());

        page.Find("#voice-message").TextContent.Should().StartWith("Valider ne se fait jamais à la voix");
    }

    [Fact]
    public async Task F8_maintenue_dicte_dans_le_champ_qui_a_le_focus_et_hors_champ_donne_une_commande()
    {
        _speech.Text = "À réception de facture.";
        IRenderedComponent<Coquille> page = Rendre();
        page.WaitForAssertion(() => page.Find("#subject-mic"));
        VoiceHost hote = page.FindComponent<VoiceHost>().Instance;

        await page.InvokeAsync(() => hote.OnPushToTalk(true, "payment-terms"));
        page.Find("#voice-hud").TextContent.Should().Contain("relâchez F8");
        await page.InvokeAsync(() => hote.OnPushToTalk(false, null));

        _voice.Invocations.Should().Contain(i => i.Identifier == "insertText" && Equals(i.Arguments[0], "payment-terms"));
        _voice.Invocations.Should().Contain(i => i.Identifier == "listenPushToTalk");

        _speech.Command = new VoiceCommand { Kind = VoiceCommandKind.Create, Phrase = "nouveau devis", Target = VoiceTarget.Quotes };
        await page.InvokeAsync(() => hote.OnPushToTalk(true, null));
        await page.InvokeAsync(() => hote.OnPushToTalk(false, null));

        page.WaitForAssertion(() => page.Find("#voice-command").TextContent.Should().Be("Créer : Devis"));
        _speech.Calls.Last().Mode.Should().Be(SpeechMode.Command);
    }

    [Fact]
    public async Task La_palette_propose_de_parler_et_de_masquer_les_micros()
    {
        IRenderedComponent<Coquille> page = Rendre();
        page.WaitForAssertion(() => page.Find("#subject-mic"));
        VoicePaletteProvider palette = Services.GetServices<Lettra.UI.Shared.Components.Palette.ICommandPaletteProvider>().OfType<VoicePaletteProvider>().Single();

        (await palette.SearchAsync("", CancellationToken.None)).Select(i => i.Label).Should().Equal("Parler à Lettra", "Masquer les micros des champs");
    }

    // ---------- commandes dans un devis ----------

    private sealed class Lignes
    {
        public FakeQuotesGateway Quotes { get; } = new();

        public FakeCatalogGateway Catalog { get; } = new();
    }

    private Lignes Preparer()
    {
        Lignes l = new();
        Services.AddSingleton<IQuotesGateway>(l.Quotes);
        Services.AddSingleton<IInvoicesGateway>(new FakeInvoicesGateway());
        Services.AddSingleton<IBillingSettingsGateway>(new FakeBillingSettingsGateway());
        Services.AddSingleton<ICatalogGateway>(l.Catalog);
        FakeCustomersGateway customers = new();
        customers.Customers.Add(new CustomerSummary(BillingFixtures.DupontId, CustomerKindDto.Company, "Dupont SARL", "732829320", "Lyon"));
        Services.AddSingleton<ICustomersGateway>(customers);
        Services.AddSingleton<IFileDownloader>(new FakeFileDownloader());
        l.Catalog.Add("TENUE", "Tenue comptable mensuelle", 250m);
        return l;
    }

    private IRenderedComponent<QuoteEditor> Ouvrir(Lignes l)
    {
        QuoteResponse devis = l.Quotes.Add(QuoteStatusDto.Draft, null,
            new DocumentLineRequest { Designation = "Conseil", UnitCode = "C62", Quantity = 1m, UnitPriceExclTax = 100m });
        return Render<QuoteEditor>(p => p.Add(e => e.Id, devis.Id).Add(e => e.AutosaveDelay, TimeSpan.Zero));
    }

    [Fact]
    public async Task Ajouter_une_ligne_a_la_voix_remplit_la_grille_et_ramene_le_TTC_en_HT()
    {
        Lignes l = Preparer();
        IRenderedComponent<QuoteEditor> page = Ouvrir(l);
        page.WaitForAssertion(() => page.FindAll(".lg-row[data-r]").Should().HaveCount(1));
        VoiceCommands commandes = Services.GetRequiredService<VoiceCommands>();

        bool fait = await page.InvokeAsync(() => commandes.TryRunAsync(new VoiceCommand
        {
            Kind = VoiceCommandKind.AddLine,
            Phrase = "ajoute trois heures d'audit à 90 euros et la tenue comptable mensuelle à 300 euros TTC",
            Lines = [new(3m, "HUR", "audit", 90m, false), new(1m, null, "tenue comptable mensuelle", 300m, true)],
        }));

        fait.Should().BeTrue();
        page.WaitForAssertion(() => l.Quotes.Updates.Last().Request.Lines.Should().HaveCount(3));
        var lignes = l.Quotes.Updates.Last().Request.Lines;
        lignes[1].Should().Match<DocumentLineRequest>(x => x.Designation == "Audit" && x.Quantity == 3m && x.UnitCode == "HUR" && x.UnitPriceExclTax == 90m);
        lignes[2].Should().Match<DocumentLineRequest>(x => x.Reference == "TENUE" && x.UnitPriceExclTax == 250m, "300 € TTC à 20 % font 250 € HT");
    }

    [Fact]
    public async Task Une_remise_a_la_voix_s_applique_a_chaque_ligne_et_sans_piece_ouverte_rien_ne_se_passe()
    {
        Lignes l = Preparer();
        VoiceCommands seules = Services.GetRequiredService<VoiceCommands>();
        VoiceCommand remise = new() { Kind = VoiceCommandKind.ApplyDiscount, Phrase = "remise de dix pour cent", DiscountPercent = 10m };
        (await seules.TryRunAsync(remise)).Should().BeFalse("aucune pièce ouverte");

        IRenderedComponent<QuoteEditor> page = Ouvrir(l);
        page.WaitForAssertion(() => page.FindAll(".lg-row[data-r]").Should().HaveCount(1));

        (await page.InvokeAsync(() => seules.TryRunAsync(remise))).Should().BeTrue();

        page.WaitForAssertion(() => l.Quotes.Updates.Last().Request.Lines.Single().DiscountPercent.Should().Be(10m));
    }

    [Fact]
    public void Les_lignes_d_un_devis_dicte_attendent_l_ouverture_du_devis()
    {
        Lignes l = Preparer();
        VoiceCommands commandes = Services.GetRequiredService<VoiceCommands>();
        commandes.Defer(new VoiceCommand { Kind = VoiceCommandKind.AddLine, Phrase = "devis pour Dupont, deux jours d'audit", Lines = [new(2m, "DAY", "audit", 900m, false)] });

        IRenderedComponent<QuoteEditor> page = Ouvrir(l);

        page.WaitForAssertion(() => l.Quotes.Updates.Should().NotBeEmpty());
        page.WaitForAssertion(() => l.Quotes.Updates.Last().Request.Lines.Should().Contain(x => x.Designation == "Audit" && x.UnitCode == "DAY" && x.Quantity == 2m && x.UnitPriceExclTax == 900m));
        commandes.Deferred.Should().BeNull();
    }

    [Fact]
    public void Une_commande_se_decrit_en_francais()
    {
        var l = Services.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<SharedResource>>();
        VoiceCommands.Describe(new VoiceCommand { Kind = VoiceCommandKind.AddLine, Phrase = "x", Lines = [new(1.5m, "DAY", "audit", 900m, false)] }, l)
            .Should().Be("Ajouter une ligne : 1,5 j audit à 900,00 € HT");
        VoiceCommands.Describe(new VoiceCommand { Kind = VoiceCommandKind.Search, Phrase = "x", Term = "Dupont", Target = VoiceTarget.Customers }, l)
            .Should().Be("Chercher « Dupont » dans : Clients");
        VoiceCommands.Describe(new VoiceCommand { Kind = VoiceCommandKind.DraftQuote, Phrase = "x", Customer = "Dupont", Lines = [new(1m, null, "audit", null, null)] }, l)
            .Should().Be("Nouveau devis pour Dupont (1 lignes à relire)");
    }
}

internal sealed class FakeSpeechGateway : ISpeechGateway
{
    public SpeechStatusResponse Status { get; set; } = new(true, "whisper-serveur", false, ["fr", "en"], 30);

    public string? StatusError { get; set; }

    public string Text { get; set; } = "Bonjour.";

    public VoiceCommand? Command { get; set; }

    public List<(SpeechMode Mode, string Language, int Bytes)> Calls { get; } = [];

    public int StatusReads { get; private set; }

    public Task<OperationResult<SpeechStatusResponse>> GetStatusAsync(CancellationToken cancellationToken)
    {
        StatusReads++;
        return Task.FromResult(StatusError is { } error ? OperationResult<SpeechStatusResponse>.Failure(error) : OperationResult<SpeechStatusResponse>.Success(Status));
    }

    public Task<OperationResult<TranscriptionResponse>> TranscribeAsync(ReadOnlyMemory<byte> wav, SpeechMode mode, string language, CancellationToken cancellationToken)
    {
        Calls.Add((mode, language, wav.Length));
        return Task.FromResult(OperationResult<TranscriptionResponse>.Success(new(Text, language, 1.2, mode == SpeechMode.Command ? Command : null)));
    }
}
