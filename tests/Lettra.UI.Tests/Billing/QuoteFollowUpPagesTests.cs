using System.Globalization;
using Bunit;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Billing;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Billing;
using Lettra.UI.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Billing;

/// <summary>Devis à relancer et carnet de commandes : liste triée et filtrée, relance relue puis envoyée, fenêtre, historique, carnet expliqué.</summary>
[Collection("culture")]
public sealed class QuoteFollowUpPagesTests : BunitContext
{
    private readonly FakeQuoteFollowUpsGateway _gateway = new();

    public QuoteFollowUpPagesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IQuoteFollowUpsGateway>(_gateway);
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<QuoteFollowUpPage> Open()
    {
        IRenderedComponent<QuoteFollowUpPage> page = Render<QuoteFollowUpPage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    private IRenderedComponent<OrderBookPage> OpenBook()
    {
        IRenderedComponent<OrderBookPage> page = Render<OrderBookPage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    private static OrderBookResponse Book(bool enough = true, params OrderBookQuoteResponse[] quotes) => new(
        new DateOnly(2026, 10, 16), new DateOnly(2024, 10, 16), 24, 3, enough ? 6 : 1, enough ? 10 : 2, enough ? 0.6m : 0.5m, enough,
        quotes.Sum(q => q.TotalExclTax), quotes.Sum(q => q.WeightedExclTax), quotes.Sum(q => q.WeightedInclTax),
        [new(QuoteAmountBandDto.Small, 0, 0, null), new(QuoteAmountBandDto.Medium, 6, 10, 0.6m), new(QuoteAmountBandDto.Large, 0, 0, null), new(QuoteAmountBandDto.VeryLarge, 0, 0, null)],
        [new(QuoteAgeBandDto.FirstWeek, 6, 10, 0.6m), new(QuoteAgeBandDto.SecondWeek, 2, 5, 0.4m), new(QuoteAgeBandDto.SameMonth, 0, 1, 0m), new(QuoteAgeBandDto.Older, 0, 0, null)],
        quotes);

    [Fact]
    public void Les_devis_a_relancer_montrent_leur_fin_leur_montant_et_leur_chance()
    {
        _gateway.Quotes.Add(FakeQuoteFollowUpsGateway.Quote("DEV-0007", "Dupont SARL", "compta@dupont.fr", 3));
        _gateway.Quotes.Add(FakeQuoteFollowUpsGateway.Quote("DEV-0002", "Marie Durand", null, 0, 800m));
        _gateway.Quotes.Add(FakeQuoteFollowUpsGateway.Quote("DEV-0009", "Hôtel Lefèvre", "hotel@lefevre.fr", 1, due: false));

        IRenderedComponent<QuoteFollowUpPage> page = Open();

        page.Find("#quote-follow-ups-due").TextContent.Should().Be("2");
        page.Find("#quote-follow-ups-due-amount").TextContent.Should().Contain("2").And.Contain("000");
        page.Find("#quotes-tab-followups").GetAttribute("aria-current").Should().Be("page");
        page.Find(".lt-quotes-tab-count").TextContent.Should().Be("2");
        page.FindAll("#quote-follow-ups-table tbody tr").Should().HaveCount(2, "seuls les devis à relancer s'affichent d'abord");
        page.Find("#quote-follow-ups-table").ClassList.Should().Contain("lt-cards");
        page.Find("#quote-follow-ups-table tbody tr a").TextContent.Should().Be("DEV-0002", "le plus proche de sa fin d'abord");
        page.Markup.Should().Contain("se termine aujourd'hui").And.Contain("se termine dans 3 jours").And.Contain("Pas d'adresse e-mail").And.Contain("50 %");

        page.Find("#quote-follow-ups-due-only").Change(false);
        page.FindAll("#quote-follow-ups-table tbody tr").Should().HaveCount(3);
        page.Markup.Should().Contain("se termine demain").And.Contain("1 fois").And.Contain("Préparer une relance");
        page.Find("#quote-follow-ups-with-email").Change(true);
        page.Find("#quote-follow-ups-count").TextContent.Should().Be("2 devis sur 3");
        page.Find("#quote-follow-ups-search").Input("lefevre");
        page.FindAll("#quote-follow-ups-table tbody tr").Should().ContainSingle();
        page.Find("#quote-follow-ups-search").Input("zzz");
        page.Find("#quote-follow-ups-nomatch").TextContent.Should().Be("Aucun devis ne correspond à ces filtres.");
        page.Find("#quote-follow-ups-filters-reset").Click();
        page.Find("#quote-follow-ups-sort-amount").Click();
        page.FindAll("#quote-follow-ups-table tbody td:first-child a").Select(a => a.TextContent).Should().Equal("DEV-0007", "DEV-0009", "DEV-0002");
        page.Find("#quote-follow-ups-sort").Change("Customer:Ascending");
        page.Find("#quote-follow-ups-table tbody tr a").TextContent.Should().Be("DEV-0007");
    }

    [Fact]
    public void Une_longue_liste_se_pagine()
    {
        for (int i = 1; i <= 30; i++)
        {
            _gateway.Quotes.Add(FakeQuoteFollowUpsGateway.Quote($"DEV-{i:0000}", "Dupont SARL", "compta@dupont.fr", i % 5));
        }

        IRenderedComponent<QuoteFollowUpPage> page = Open();

        page.FindAll("#quote-follow-ups-table tbody tr").Should().HaveCount(25);
        page.FindAll("#quote-follow-ups-pager button")[1].Click();
        page.FindAll("#quote-follow-ups-table tbody tr").Should().HaveCount(5);
        page.FindAll("#quote-follow-ups-pager button")[0].Click();
        page.FindAll("#quote-follow-ups-table tbody tr").Should().HaveCount(25);
    }

    [Fact]
    public void Sans_devis_en_attente_l_ecran_le_dit()
    {
        IRenderedComponent<QuoteFollowUpPage> page = Open();

        page.Find("#quote-follow-ups-empty").TextContent.Should().Contain("Aucun devis en attente de réponse");
        page.Find("#quote-follow-ups-history-empty").TextContent.Should().Contain("Aucune relance de devis");
        page.FindAll(".lt-quotes-tab-count").Should().BeEmpty();
    }

    [Fact]
    public void La_relance_se_relit_se_corrige_puis_part_par_e_mail_avec_un_message()
    {
        QuoteToFollowUpResponse quote = FakeQuoteFollowUpsGateway.Quote("DEV-0007", "Dupont SARL", "compta@dupont.fr", 3);
        _gateway.Quotes.Add(quote);
        IRenderedComponent<QuoteFollowUpPage> page = Open();

        page.Find("#quote-follow-ups-open-DEV-0007").Click();

        page.WaitForAssertion(() => page.Find("#quote-follow-ups-subject").GetAttribute("value").Should().StartWith("Votre devis DEV-0007"));
        page.Find("#quote-follow-ups-preview-to").TextContent.Should().Be("compta@dupont.fr");
        page.Find("#quote-follow-ups-preview-title").TextContent.Should().Be("Relance du devis DEV-0007 pour Dupont SARL");
        page.Find("#quote-follow-ups-subject").Input("Notre proposition");
        page.Find("#quote-follow-ups-body").Input("Bonjour,\nOù en êtes-vous ?");

        page.Find("#quote-follow-ups-send").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Relance du devis DEV-0007 envoyée à compta@dupont.fr."));
        (Guid id, SendQuoteFollowUpRequest request) = _gateway.Sends.Should().ContainSingle().Subject;
        (id, request.Channel, request.Subject, request.Body).Should().Be((quote.QuoteId, QuoteFollowUpChannelDto.Email, "Notre proposition", "Bonjour,\nOù en êtes-vous ?"));
        page.FindAll("#quote-follow-ups-preview").Should().BeEmpty();
        page.WaitForAssertion(() => page.FindAll("#quote-follow-ups-history-table tbody tr").Should().ContainSingle());
        page.Find("#quote-follow-ups-due").TextContent.Should().Be("0");
    }

    [Fact]
    public void Un_objet_vide_bloque_l_envoi_et_l_apercu_se_ferme()
    {
        _gateway.Quotes.Add(FakeQuoteFollowUpsGateway.Quote("DEV-0007", "Dupont SARL", "compta@dupont.fr", 3));
        IRenderedComponent<QuoteFollowUpPage> page = Open();
        page.Find("#quote-follow-ups-open-DEV-0007").Click();
        page.WaitForAssertion(() => page.FindAll("#quote-follow-ups-send").Should().ContainSingle());

        page.Find("#quote-follow-ups-subject").Input(" ");

        page.Find("#quote-follow-ups-send").HasAttribute("disabled").Should().BeTrue();
        page.Find("#quote-follow-ups-manual").HasAttribute("disabled").Should().BeTrue();
        page.Find("#quote-follow-ups-preview button[aria-label]").Click();
        page.FindAll("#quote-follow-ups-preview").Should().BeEmpty();
    }

    [Fact]
    public void Sans_serveur_d_envoi_l_ecran_le_signale_et_la_relance_se_note_a_la_main()
    {
        _gateway.Settings = _gateway.Settings with { EmailConfigured = false };
        QuoteToFollowUpResponse quote = FakeQuoteFollowUpsGateway.Quote("DEV-0007", "Dupont SARL", "compta@dupont.fr", 3);
        _gateway.Quotes.Add(quote);
        IRenderedComponent<QuoteFollowUpPage> page = Open();

        page.Find("#quote-follow-ups-no-email").TextContent.Should().Contain("n'envoie pas d'e-mails");
        page.Find("#quote-follow-ups-open-DEV-0007").Click();
        page.WaitForAssertion(() => page.FindAll("#quote-follow-ups-manual").Should().ContainSingle());
        page.FindAll("#quote-follow-ups-send").Should().BeEmpty();

        page.Find("#quote-follow-ups-manual").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Relance du devis DEV-0007 notée."));
        _gateway.Sends.Single().Request.Channel.Should().Be(QuoteFollowUpChannelDto.Manual);
    }

    [Fact]
    public void Un_envoi_refuse_ou_un_apercu_impossible_s_affichent_dans_le_toaster()
    {
        _gateway.Quotes.Add(FakeQuoteFollowUpsGateway.Quote("DEV-0007", "Dupont SARL", "compta@dupont.fr", 3));
        IRenderedComponent<QuoteFollowUpPage> page = Open();
        page.Find("#quote-follow-ups-open-DEV-0007").Click();
        page.WaitForAssertion(() => page.FindAll("#quote-follow-ups-send").Should().ContainSingle());
        _gateway.FailWith = ErrorCodes.Email.SendFailed;

        page.Find("#quote-follow-ups-send").Click();

        page.WaitForAssertion(() => this.Toasts().Should().ContainSingle(t => t.Kind == ToastKind.Error));
        page.FindAll("#quote-follow-ups-preview").Should().ContainSingle("l'aperçu reste ouvert pour réessayer");

        _gateway.FailWith = ErrorCodes.QuoteFollowUps.CompanyProfileMissing;
        page.Find("#quote-follow-ups-open-DEV-0007").Click();
        page.WaitForAssertion(() => this.Toasts().Should().HaveCount(2));
        this.ToastText().Should().Contain("fiche de l'entreprise");
    }

    [Fact]
    public void La_fiche_d_un_devis_ouvre_directement_sa_relance()
    {
        QuoteToFollowUpResponse quote = FakeQuoteFollowUpsGateway.Quote("DEV-0007", "Dupont SARL", "compta@dupont.fr", 3);
        _gateway.Quotes.Add(quote);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"quote-follow-ups?quote={quote.QuoteId}");

        IRenderedComponent<QuoteFollowUpPage> page = Open();

        page.WaitForAssertion(() => page.Find("#quote-follow-ups-preview-title").TextContent.Should().Contain("DEV-0007"));
    }

    [Fact]
    public void La_fenetre_se_regle_et_s_enregistre_avec_un_message()
    {
        IRenderedComponent<QuoteFollowUpPage> page = Open();

        page.Find("#quote-follow-ups-window").Change("10");
        page.Find("#quote-follow-ups-settings-save").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Fenêtre de relance enregistrée : 10 jours."));
        _gateway.SavedSettings.Should().ContainSingle().Which.WindowDays.Should().Be(10);
        page.Markup.Should().Contain("dans les 10 prochains jours");

        _gateway.FailWith = ErrorCodes.QuoteFollowUps.WindowInvalid;
        page.Find("#quote-follow-ups-settings-save").Click();
        page.WaitForAssertion(() => this.Toasts().Should().Contain(t => t.Kind == ToastKind.Error));
    }

    [Fact]
    public void L_historique_se_filtre_se_trie_et_se_pagine_sur_le_serveur()
    {
        for (int i = 0; i < 30; i++)
        {
            _gateway.History.Add(FakeQuoteFollowUpsGateway.Sent($"DEV-{i:0000}", "Dupont SARL", i % 2 == 0 ? QuoteFollowUpChannelDto.Email : QuoteFollowUpChannelDto.Manual,
                i == 1 ? QuoteFollowUpOutcomeDto.Failed : QuoteFollowUpOutcomeDto.Sent));
        }

        IRenderedComponent<QuoteFollowUpPage> page = Open();

        page.FindAll("#quote-follow-ups-history-table tbody tr").Should().HaveCount(25);
        page.Markup.Should().Contain("En échec").And.Contain("À la main");
        page.FindAll("#quote-follow-ups-history-pager button")[1].Click();
        page.WaitForAssertion(() => page.FindAll("#quote-follow-ups-history-table tbody tr").Should().HaveCount(5));
        page.FindAll("#quote-follow-ups-history-pager button")[0].Click();
        page.WaitForAssertion(() => _gateway.HistoryQueries[^1].Page.Should().Be(1));

        page.Find("#quote-follow-ups-history-channel").Change("Manual");
        page.Find("#quote-follow-ups-history-outcome").Change("Failed");
        page.Find("#quote-follow-ups-history-dates-from").Change("2026-10-01");
        page.Find("#quote-follow-ups-history-dates-to").Change("2026-10-31");
        page.Find("#quote-follow-ups-history-search").Change("dupont");
        page.Find("#quote-follow-ups-history-sort").Change("Customer:Ascending");

        page.WaitForAssertion(() => _gateway.HistoryQueries[^1].Should().Be(new QuoteFollowUpHistoryQuery(
            null, QuoteFollowUpChannelDto.Manual, QuoteFollowUpOutcomeDto.Failed, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31),
            "dupont", QuoteFollowUpSortDto.Customer, false, 1, 25)));
        page.Find("#quote-follow-ups-history-sort-date").Click();
        page.WaitForAssertion(() => _gateway.HistoryQueries[^1].Should().Match<QuoteFollowUpHistoryQuery>(q => q.Sort == QuoteFollowUpSortDto.Date && q.Descending));
        page.Find("#quote-follow-ups-history-filters-reset").Click();
        page.WaitForAssertion(() => _gateway.HistoryQueries[^1].Should().Be(new QuoteFollowUpHistoryQuery(
            null, null, null, null, null, null, QuoteFollowUpSortDto.Date, true, 1, 25)));
    }

    [Fact]
    public void Sur_la_fiche_d_un_devis_l_historique_ne_montre_que_ses_relances_et_rien_sans_relance()
    {
        QuoteFollowUpResponse sent = FakeQuoteFollowUpsGateway.Sent("DEV-0007", "Dupont SARL");
        _gateway.History.Add(sent);

        IRenderedComponent<QuoteFollowUpHistory> history = Render<QuoteFollowUpHistory>(p => p.Add(h => h.QuoteId, sent.QuoteId).Add(h => h.Id, "quote-follow-up-history"));
        IRenderedComponent<QuoteFollowUpHistory> none = Render<QuoteFollowUpHistory>(p => p.Add(h => h.QuoteId, Guid.CreateVersion7()));

        history.WaitForAssertion(() => history.FindAll("#quote-follow-up-history-table tbody tr").Should().ContainSingle());
        history.FindAll("#quote-follow-up-history-filters").Should().BeEmpty("un seul devis n'a pas besoin de filtres");
        history.FindAll("th").Should().HaveCount(4);
        none.Markup.Trim().Should().BeEmpty();
    }

    [Fact]
    public void Le_carnet_de_commandes_montre_le_montant_pondere_et_son_explication()
    {
        _gateway.Book = Book(true,
            FakeQuoteFollowUpsGateway.BookQuote("DEV-0003", "Dupont SARL", 4_000m, 0.55m),
            FakeQuoteFollowUpsGateway.BookQuote("DEV-0004", "Marie Durand", 500m, 0.5m, age: 3, customerGlobal: true));

        IRenderedComponent<OrderBookPage> page = OpenBook();

        page.Find("#quotes-tab-orderbook").GetAttribute("aria-current").Should().Be("page");
        page.Find("#order-book-weighted").TextContent.Should().Contain("450");
        page.Find("#order-book-rate").TextContent.Should().Be("60 %");
        page.FindAll("#order-book-not-enough").Should().BeEmpty();
        page.FindAll("#order-book-by-amount tbody tr").Should().HaveCount(4);
        page.Find("#order-book-by-amount").TextContent.Should().Contain("1 000 à 5 000 €").And.Contain("6 sur 10").And.Contain("aucun devis");
        page.Find("#order-book-notice").TextContent.Should().Contain("ni montré au client");
        page.FindAll("#order-book-table tbody tr a").Select(a => a.TextContent).Should().Equal("DEV-0003", "DEV-0004");
        page.Find("#order-book-why-DEV-0004").TextContent.Should().Contain("Client Marie Durand").And.Contain("(trop peu de devis : taux global)")
            .And.Contain("Moyenne : 50 %");

        page.Find("#order-book-filter-amount").Change("Small");
        page.FindAll("#order-book-table tbody tr").Should().ContainSingle();
        page.Find("#order-book-filter-age").Change("SecondWeek");
        page.Find("#order-book-nomatch").TextContent.Should().Be("Aucun devis ne correspond à ces filtres.");
        page.Find("#order-book-filters-reset").Click();
        page.Find("#order-book-search").Input("durand");
        page.Find("#order-book-count").TextContent.Should().Be("1 devis sur 2");
        page.Find("#order-book-search").Input("");
        page.Find("#order-book-sort-number").Click();
        page.FindAll("#order-book-table tbody tr a").Select(a => a.TextContent).Should().Equal("DEV-0003", "DEV-0004");
        page.Find("#order-book-sort").Change("Age:Ascending");
        page.Find("#order-book-table tbody tr a").TextContent.Should().Be("DEV-0004");
    }

    [Fact]
    public void Un_historique_court_est_signale_et_une_longue_liste_se_pagine()
    {
        _gateway.Book = Book(false, [.. Enumerable.Range(1, 30).Select(i => FakeQuoteFollowUpsGateway.BookQuote($"DEV-{i:0000}", "Dupont SARL", 1_000m + i, 0.5m))]);

        IRenderedComponent<OrderBookPage> page = OpenBook();

        page.Find("#order-book-not-enough").TextContent.Should().Contain("moins de 3 devis conclus depuis le 16/10/2024");
        page.FindAll("#order-book-table tbody tr").Should().HaveCount(25);
        page.FindAll("#order-book-pager button")[1].Click();
        page.FindAll("#order-book-table tbody tr").Should().HaveCount(5);
        page.FindAll("#order-book-pager button")[0].Click();
        page.FindAll("#order-book-table tbody tr").Should().HaveCount(25);
    }

    [Fact]
    public void Un_carnet_vide_ou_refuse_le_dit()
    {
        _gateway.Book = Book(false);
        OpenBook().Find("#order-book-empty").TextContent.Should().Contain("Aucun devis en attente");

        _gateway.Book = null;
        IRenderedComponent<OrderBookPage> refused = OpenBook();
        refused.FindAll("#order-book-empty").Should().ContainSingle();
        refused.WaitForAssertion(() => this.Toasts().Should().Contain(t => t.Kind == ToastKind.Error, "l'erreur de lecture s'affiche dans le toaster"));
    }

    [Fact]
    public void Les_onglets_des_devis_menent_aux_trois_ecrans()
    {
        IRenderedComponent<QuotesTabs> tabs = Render<QuotesTabs>(p => p.Add(t => t.Current, QuotesTab.All).Add(t => t.DueCount, 4));

        tabs.FindAll("a").Select(a => a.GetAttribute("href")).Should().Equal("quotes", "quote-follow-ups", "order-book");
        tabs.Find(".lt-quotes-tab-count").GetAttribute("aria-label").Should().Be("4 devis à relancer");
        tabs.Find("#quotes-tab-all").GetAttribute("aria-current").Should().Be("page");
    }
}
