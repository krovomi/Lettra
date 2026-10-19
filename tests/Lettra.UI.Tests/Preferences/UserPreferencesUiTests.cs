using System.Globalization;
using System.Text.Json;
using Bunit;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Identity;
using Lettra.Contracts.Preferences;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Preferences;
using Lettra.UI.Shared.Keyboard;
using Lettra.UI.Shared.Layout;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Billing;
using Lettra.UI.Shared.Pages.Settings;
using Lettra.UI.Shared.Preferences;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Billing;
using Lettra.UI.Tests.Cabinet;
using Lettra.UI.Tests.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Preferences;

/// <summary>
/// Les préférences vues des écrans : rangées dans le compte quand le serveur le permet, sinon sur l'appareil ; valeurs
/// pré-remplies, « Retenir par défaut », suggestions classées par usage et « Reprendre où j'en étais ».
/// </summary>
[Collection("culture")]
public sealed class UserPreferencesUiTests : BunitContext
{
    private static readonly Guid Dossier = Guid.CreateVersion7();
    private static readonly string DeviceKey = "lettra.preferences." + UserStorageKey.Hash("Oidc|Claire");

    private readonly FakeUserPreferencesGateway _account = new();
    private readonly BunitJSModuleInterop _module;

    public UserPreferencesUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _module = JSInterop.SetupModule(UiModule.Path);
        Services.AddSingleton<ISessionGateway>(new FakeSessionGateway(new SessionResponse(true, SessionModeDto.Oidc, "Claire", true)));
        Services.AddSingleton<IUserPreferencesGateway>(_account);
        Services.AddSingleton<IDossierSelection>(new FixedDossierSelection(Dossier));
        Services.AddSingleton<ICabinetGateway>(new FakeCabinetGateway());
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero)));
        Services.AddLettraUi();
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private async Task<UserPreferencesState> StateAsync()
    {
        await Services.GetRequiredService<SessionState>().RefreshAsync();
        UserPreferencesState state = Services.GetRequiredService<UserPreferencesState>();
        await state.EnsureLoadedAsync();
        return state;
    }

    private string? LastDeviceWrite() =>
        _module.Invocations.LastOrDefault(i => i.Identifier == "writePreference" && Equals(i.Arguments[0], DeviceKey)).Arguments?[1] as string;

    [Fact]
    public async Task Sans_compte_les_preferences_restent_sur_l_appareil()
    {
        _account.Available = false;
        UserPreferencesState state = await StateAsync();

        IReadOnlyList<Contracts.Common.ErrorDetail> errors = await state.UpdateDefaultsAsync(Dossier, d => d with { Unit = "HUR" });

        errors.Should().BeEmpty();
        state.InAccount.Should().BeFalse();
        state.DefaultsFor(Dossier).Unit.Should().Be("HUR");
        _account.Saves.Should().BeEmpty();
        LastDeviceWrite().Should().Contain("\"HUR\"", "la copie de l'appareil garde le réglage, par utilisateur");
    }

    [Fact]
    public async Task Ce_que_l_appareil_retenait_est_range_dans_le_compte_au_premier_passage()
    {
        UserPreferencesResponse local = new(new WorkspacePreferencesDto { Theme = "Dark" }, [new EntryDefaultsDto { DossierId = Dossier, Unit = "DAY" }], [], null, null);
        _module.Setup<string?>("readPreference", DeviceKey).SetResult(JsonSerializer.Serialize(local, ContractsJsonContext.Default.UserPreferencesResponse));

        UserPreferencesState state = await StateAsync();

        state.InAccount.Should().BeTrue();
        _account.Stored.Workspace.Theme.Should().Be("Dark");
        _account.Stored.EntryDefaults.Should().ContainSingle().Which.Unit.Should().Be("DAY");
        state.Current.Version.Should().Be(1);
    }

    [Fact]
    public async Task Un_autre_appareil_qui_a_ecrit_entre_temps_ne_fait_rien_perdre()
    {
        _account.WriteFromAnotherDevice(p => p);
        UserPreferencesState state = await StateAsync();
        _account.WriteFromAnotherDevice(p => p with { Workspace = p.Workspace with { HomePage = "invoices" } });

        (await state.UpdateDefaultsAsync(Dossier, d => d with { QuoteValidityDays = 45 })).Should().BeEmpty();

        _account.Saves.Select(s => s.Version).Should().Equal(1, 2);
        _account.Stored.Workspace.HomePage.Should().Be("invoices", "le réglage de l'autre appareil est relu puis gardé");
        state.DefaultsFor(Dossier).QuoteValidityDays.Should().Be(45);
    }

    [Fact]
    public async Task Serveur_injoignable_a_l_ecriture_le_changement_reste_sur_l_appareil()
    {
        UserPreferencesState state = await StateAsync();
        _account.WritesUnreachable = true;

        (await state.UpdateWorkspaceAsync(w => w with { RankByUsage = false })).Should().BeEmpty();

        state.InAccount.Should().BeFalse();
        state.Workspace.RankByUsage.Should().BeFalse();
        LastDeviceWrite().Should().Contain("\"rankByUsage\":false");
    }

    [Fact]
    public async Task Les_choix_les_plus_frequents_remontent_en_tete()
    {
        UserPreferencesState state = await StateAsync();
        Guid[] clients = [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()];
        Guid facture = Guid.CreateVersion7();
        await state.RecordUseAsync(Dossier, UsageKindDto.Customer, clients[2]);
        await state.RecordUseAsync(Dossier, UsageKindDto.Customer, clients[2]);
        await state.RecordUseAsync(Dossier, UsageKindDto.Customer, clients[1]);
        await state.RecordUseAsync(Dossier, UsageKindDto.Invoice, facture);
        await state.RecordUseAsync(null, UsageKindDto.Customer, clients[0]);

        state.RankItems(clients, c => c, Dossier, UsageKindDto.Customer).Should().Equal(clients[2], clients[1], clients[0]);
        state.RankItems(clients, c => c, Guid.CreateVersion7(), UsageKindDto.Customer).Should().Equal(clients, "un autre dossier n'a rien compté");

        await state.UpdateWorkspaceAsync(w => w with { RankByUsage = false });
        state.RankItems(clients, c => c, Dossier, UsageKindDto.Customer).Should().Equal(clients, "le classement est coupé");
        state.Ranked(Dossier, UsageKindDto.Invoice).Should().Equal([facture], "les dernières pièces ouvertes restent proposées");
    }

    [Fact]
    public async Task Hors_compte_les_choix_sont_comptes_sur_l_appareil()
    {
        _account.Available = false;
        UserPreferencesState state = await StateAsync();
        Guid souvent = Guid.CreateVersion7();
        Guid parfois = Guid.CreateVersion7();

        await state.RecordUseAsync(Dossier, UsageKindDto.CatalogItem, parfois);
        await state.RecordUseAsync(Dossier, UsageKindDto.CatalogItem, souvent);
        await state.RecordUseAsync(Dossier, UsageKindDto.CatalogItem, souvent);
        for (int i = 0; i < UserPreferencesState.MaxDocumentsPerKind + 2; i++)
        {
            await state.RecordUseAsync(Dossier, UsageKindDto.Quote, Guid.CreateVersion7());
        }

        state.Ranked(Dossier, UsageKindDto.CatalogItem).Should().Equal(souvent, parfois);
        state.Ranked(Dossier, UsageKindDto.Quote).Should().HaveCount(UserPreferencesState.MaxDocumentsPerKind);

        await state.ForgetUsageAsync(Dossier);
        state.Ranked(Dossier, UsageKindDto.CatalogItem).Should().BeEmpty();
    }

    [Fact]
    public async Task La_page_mes_preferences_enregistre_les_valeurs_de_saisie_et_efface_tout()
    {
        Services.AddSingleton<IBillingSettingsGateway>(new FakeBillingSettingsGateway());
        await StateAsync();
        IRenderedComponent<MyPreferences> page = Render<MyPreferences>();

        page.Find("#preferences-where").ClassList.Should().Contain("is-account");
        page.Find("#pref-unit").Change("HUR");
        page.Find("#pref-validity").Change("45");
        page.Find("#pref-quote-notes").Change("  Devis valable sous réserve d'acceptation.  ");
        await page.Find("#pref-entry-save").ClickAsync(new());

        this.ToastText().Should().Be("Préférences enregistrées.");
        _account.Stored.EntryDefaults.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new EntryDefaultsDto { DossierId = Dossier, Unit = "HUR", QuoteValidityDays = 45, QuoteNotes = "Devis valable sous réserve d'acceptation." });

        await page.Find("#pref-rank").ChangeAsync(new ChangeEventArgs { Value = false });
        _account.Stored.Workspace.RankByUsage.Should().BeFalse();
        await page.Find("#pref-home").ChangeAsync(new ChangeEventArgs { Value = "invoices" });
        _account.Stored.Workspace.HomePage.Should().Be("invoices");
        await page.Find("#pref-forget-all").ClickAsync(new());
        this.ToastText().Should().Be("Suggestions oubliées.");

        await page.Find("#pref-reset").ClickAsync(new());
        await page.Find("#pref-reset-confirm").ClickAsync(new());

        this.ToastText().Should().Be("Préférences réinitialisées.");
        _account.Stored.EntryDefaults.Should().BeEmpty();
        _account.Stored.Workspace.HomePage.Should().BeNull();
        page.Find("#pref-unit").GetAttribute("value").Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Sans_compte_la_page_dit_que_les_preferences_valent_sur_cet_appareil()
    {
        _account.Available = false;
        await StateAsync();

        IRenderedComponent<MyPreferences> page = Render<MyPreferences>();

        page.Find("#preferences-where").ClassList.Should().Contain("is-device");
        page.Find("#pref-home").QuerySelectorAll("option").Select(o => o.GetAttribute("value"))
            .Should().Contain("invoices").And.Contain("settings/preferences").And.NotContain("cabinet/dossier", "seuls les écrans du menu sont proposés");
    }

    [Fact]
    public async Task Retenir_par_defaut_n_apparait_que_pour_une_valeur_nouvelle()
    {
        await StateAsync();
        IRenderedComponent<RememberDefault> button = Render<RememberDefault>(p => p
            .Add(c => c.Field, RememberedField.PaymentTerms)
            .Add(c => c.Value, "Paiement à 15 jours"));

        await button.Find("button").ClickAsync(new());

        this.ToastText().Should().Be("Retenu pour vos prochaines pièces de ce dossier.");
        _account.Stored.EntryDefaults.Should().ContainSingle().Which.PaymentTerms.Should().Be("Paiement à 15 jours");
        button.Render();
        button.FindAll("button").Should().BeEmpty("la valeur est déjà celle retenue");

        button.Render(p => p.Add(c => c.Value, "  "));
        button.FindAll("button").Should().BeEmpty("rien à retenir");
        button.Render(p => p.Add(c => c.Value, "Paiement comptant").Add(c => c.Disabled, true));
        button.FindAll("button").Should().BeEmpty("pièce en lecture seule");
    }

    [Fact]
    public void Les_champs_retenus_se_lisent_et_s_ecrivent()
    {
        EntryDefaultsDto defaults = new();
        foreach (RememberedField field in Enum.GetValues<RememberedField>())
        {
            defaults = RememberedFields.Write(defaults, field, field.ToString());
            RememberedFields.Read(defaults, field).Should().Be(field.ToString());
        }
    }

    [Fact]
    public async Task L_accueil_propose_de_reprendre_les_dernieres_pieces()
    {
        FakeInvoicesGateway invoices = new();
        FakeQuotesGateway quotes = new();
        Services.AddSingleton<IInvoicesGateway>(invoices);
        Services.AddSingleton<IQuotesGateway>(quotes);
        InvoiceResponse facture = invoices.Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Issued, "FAC-2026-0042");
        QuoteResponse devis = quotes.Add(QuoteStatusDto.Draft);
        UserPreferencesState state = await StateAsync();
        await state.RecordUseAsync(Dossier, UsageKindDto.Invoice, facture.Id);
        await state.RecordUseAsync(Dossier, UsageKindDto.Quote, devis.Id);
        await state.RecordUseAsync(Dossier, UsageKindDto.Invoice, Guid.CreateVersion7());

        IRenderedComponent<ResumeWork> card = Render<ResumeWork>();

        card.WaitForAssertion(() => card.FindAll(".lt-resume-item").Select(a => a.GetAttribute("href"))
            .Should().Equal($"quotes/{devis.Id}", $"invoices/{facture.Id}"));
        card.Find(".lt-resume-item").TextContent.Should().Contain("Devis");
        card.FindAll(".lt-resume-item")[1].TextContent.Should().Contain("FAC-2026-0042");

        IReadOnlyList<Shared.Components.Palette.PaletteItem> palette = await Services.GetServices<Shared.Components.Palette.ICommandPaletteProvider>().OfType<RecentDocumentsPaletteProvider>().Single().SearchAsync("0042", default);
        palette.Should().ContainSingle().Which.Label.Should().Contain("FAC-2026-0042");
        await palette[0].Run();
        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith($"invoices/{facture.Id}");
    }

    [Fact]
    public async Task Sans_piece_ouverte_l_accueil_ne_montre_rien()
    {
        await StateAsync();

        Render<ResumeWork>().Markup.Trim().Should().BeEmpty();
    }

    [Fact]
    public async Task Le_compte_regle_l_apparence_le_menu_et_l_ecran_d_accueil()
    {
        _account.WriteFromAnotherDevice(p => p with
        {
            Workspace = new WorkspacePreferencesDto { Theme = "Dark", Density = "Expert", HomePage = "invoices", NavFavorites = ["customers", "inconnu"] },
        });
        await Services.GetRequiredService<SessionState>().RefreshAsync();
        UiPreferences appearance = Services.GetRequiredService<UiPreferences>();

        await Services.GetRequiredService<PreferencesSync>().StartAsync();
        await Services.GetRequiredService<PreferencesSync>().StartAsync();

        appearance.Theme.Should().Be(UiTheme.Dark);
        appearance.Density.Should().Be(UiDensity.Expert);
        Services.GetRequiredService<NavMenuState>().Favorites.Should().Equal("customers");
        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/invoices");
        _account.Saves.Should().BeEmpty("rien n'a changé sur cet appareil");

        await Services.GetRequiredService<NavMenuState>().ToggleFavoriteAsync(new NavItem("bank", "Banque", "landmark", null));
        _account.Stored.Workspace.NavFavorites.Should().Equal(["customers", "bank", "inconnu"], "un écran inconnu ici reste en favori pour les appareils plus récents");
        _account.Stored.Workspace.HomePage.Should().Be("invoices", "le reste du compte est gardé");
    }

    [Fact]
    public async Task Le_premier_passage_range_le_menu_de_l_appareil_dans_le_compte()
    {
        _module.Setup<string?>("readPreference", "lettra.nav.favorites." + UserStorageKey.Hash("Oidc|Claire")).SetResult("catalog");
        await Services.GetRequiredService<SessionState>().RefreshAsync();

        await Services.GetRequiredService<PreferencesSync>().StartAsync();

        _account.Stored.Workspace.NavFavorites.Should().Equal("catalog");
        _account.Stored.Version.Should().NotBeNull();
    }

    [Fact]
    public async Task Un_nouveau_devis_reprend_la_validite_et_les_mentions_retenues()
    {
        Services.AddSingleton<IQuotesGateway>(new FakeQuotesGateway());
        Services.AddSingleton<IBillingSettingsGateway>(new FakeBillingSettingsGateway());
        Services.AddSingleton<ICatalogGateway>(new FakeCatalogGateway());
        Services.AddSingleton<ICustomersGateway>(new FakeCustomersGateway());
        Services.AddSingleton<IPriceListsGateway>(new FakePriceListsGateway());
        Services.AddSingleton<IFileDownloader>(new FakeFileDownloader());
        _account.WriteFromAnotherDevice(p => p with
        {
            EntryDefaults = [new EntryDefaultsDto { DossierId = Dossier, QuoteValidityDays = 45, QuoteNotes = "Offre ferme." }],
        });
        await StateAsync();

        IRenderedComponent<QuoteEditor> page = Render<QuoteEditor>(p => p.Add(e => e.AutosaveDelay, TimeSpan.Zero));

        page.WaitForAssertion(() => (page.Find("#notes").GetAttribute("value") ?? page.Find("#notes").TextContent).Should().Be("Offre ferme."));
        page.Find("#valid-until").GetAttribute("value").Should().Be("03/12/2026", "45 jours après le 19 octobre");
        page.FindAll(".lt-remember button").Should().BeEmpty("les mentions sont déjà celles retenues");
    }
}
