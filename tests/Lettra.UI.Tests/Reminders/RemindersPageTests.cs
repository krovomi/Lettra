using System.Globalization;
using Bunit;
using Lettra.Contracts.Common;
using Lettra.Contracts.Reminders;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Reminders;
using Lettra.UI.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Reminders;

/// <summary>Écran des relances (vague 7) : factures échues, aperçu puis envoi, passe du jour, scénario et historique filtré.</summary>
[Collection("culture")]
public sealed class RemindersPageTests : BunitContext
{
    private readonly FakeRemindersGateway _reminders = new();

    public RemindersPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IRemindersGateway>(_reminders);
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<RemindersPage> Open()
    {
        IRenderedComponent<RemindersPage> page = Render<RemindersPage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    [Fact]
    public void Les_factures_echues_montrent_leur_retard_leurs_penalites_et_leur_prochain_niveau()
    {
        _reminders.Invoices.Add(FakeRemindersGateway.Invoice("F2026-0007", "Dupont SARL", "compta@dupont.fr", 50));
        _reminders.Invoices.Add(FakeRemindersGateway.Invoice("F2026-0002", "Marie Durand", null, 200, next: null, dueToday: false, last: ReminderLevelDto.FormalNotice));

        IRenderedComponent<RemindersPage> page = Open();

        page.Find("#reminders-overdue").TextContent.Should().Be("2");
        page.Find("#reminders-due").TextContent.Should().Be("1");
        page.Find("#reminders-run").TextContent.Should().Contain("Envoyer les relances du jour (1)");
        page.Find("#reminders-run").HasAttribute("disabled").Should().BeFalse();
        page.FindAll("#reminders-table tbody tr").Should().HaveCount(2);
        page.Find("#reminders-table").ClassList.Should().Contain("lt-cards", "le tableau devient des cartes sur téléphone");
        page.Markup.Should().Contain("Rappel amical").And.Contain("50 j").And.Contain("Mise en demeure").And.Contain("Pas d'adresse e-mail");
        page.FindAll("#reminders-open-F2026-0002").Should().BeEmpty("rien ne suit la mise en demeure");
        page.FindAll("#reminders-no-email").Should().BeEmpty();
    }

    [Fact]
    public void Les_factures_en_retard_se_cherchent_se_filtrent_se_trient_et_se_paginent()
    {
        for (int i = 1; i <= 30; i++)
        {
            _reminders.Invoices.Add(FakeRemindersGateway.Invoice($"F2026-{i:0000}", i % 3 == 0 ? "Hôtel Lefèvre" : "Dupont SARL", "compta@client.fr", i, dueToday: i % 2 == 0));
        }

        IRenderedComponent<RemindersPage> page = Open();

        page.FindAll("#reminders-table tbody tr").Should().HaveCount(25);
        page.Find("#reminders-invoices-count").TextContent.Should().Be("30 facture(s) sur 30");
        page.Find("#reminders-table tbody tr a").TextContent.Should().Be("F2026-0030", "le plus en retard d'abord");
        page.FindAll("#reminders-invoices-pager button")[1].Click();
        page.FindAll("#reminders-table tbody tr").Should().HaveCount(5);

        page.Find("#reminders-invoices-search").Input("hotel");
        page.Find("#reminders-invoices-count").TextContent.Should().Be("10 facture(s) sur 30");
        page.Find("#reminders-invoices-due").Change(true);
        page.Find("#reminders-invoices-count").TextContent.Should().Be("5 facture(s) sur 30");
        page.Find("#reminders-invoices-sort").Change("Number");
        page.Find("#reminders-invoices-direction").Click();
        page.FindAll("#reminders-table tbody tr a").Select(a => a.TextContent).Should().Equal("F2026-0006", "F2026-0012", "F2026-0018", "F2026-0024", "F2026-0030");

        page.Find("#reminders-invoices-search").Input("zzz");
        page.Find("#reminders-invoices-nomatch").TextContent.Should().Be("Aucune facture ne correspond à ces filtres.");
    }

    [Fact]
    public void Sans_facture_echue_l_ecran_le_dit_et_la_passe_est_desactivee()
    {
        IRenderedComponent<RemindersPage> page = Open();

        page.Find("#reminders-empty").TextContent.Should().Contain("Aucune facture en retard");
        page.Find("#reminders-history-empty").TextContent.Should().Contain("Aucune relance");
        page.Find("#reminders-run").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void L_apercu_montre_le_texte_puis_l_envoi_part_par_e_mail_avec_un_message()
    {
        ReminderInvoiceResponse invoice = FakeRemindersGateway.Invoice("F2026-0007", "Dupont SARL", "compta@dupont.fr", 50);
        _reminders.Invoices.Add(invoice);
        IRenderedComponent<RemindersPage> page = Open();

        page.Find("#reminders-open-F2026-0007").Click();

        page.WaitForAssertion(() => page.Find("#reminders-preview-subject").TextContent.Should().Be("Rappel : facture F2026-0007 arrivée à échéance"));
        page.Find("#reminders-preview-to").TextContent.Should().Be("compta@dupont.fr");
        page.Find("#reminders-preview-body").TextContent.Should().StartWith("Bonjour,");

        page.Find("#reminders-send").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Relance « Rappel amical » de la facture F2026-0007 envoyée à compta@dupont.fr."));
        _reminders.Sends.Should().Equal((invoice.InvoiceId, ReminderChannelDto.Email));
        page.FindAll("#reminders-preview").Should().BeEmpty();
        page.WaitForAssertion(() => page.FindAll("#reminders-history tbody tr").Should().ContainSingle());
    }

    [Fact]
    public void Sans_serveur_d_envoi_l_ecran_le_signale_et_la_relance_se_note_a_la_main()
    {
        _reminders.Settings = _reminders.Settings with { EmailConfigured = false };
        ReminderInvoiceResponse invoice = FakeRemindersGateway.Invoice("F2026-0007", "Dupont SARL", "compta@dupont.fr", 50);
        _reminders.Invoices.Add(invoice);
        IRenderedComponent<RemindersPage> page = Open();

        page.Find("#reminders-no-email").TextContent.Should().Contain("n'envoie pas d'e-mails");
        page.Find("#reminders-run").HasAttribute("disabled").Should().BeTrue();
        page.Find("#reminders-open-F2026-0007").Click();
        page.WaitForAssertion(() => page.FindAll("#reminders-manual").Should().ContainSingle());
        page.FindAll("#reminders-send").Should().BeEmpty();

        page.Find("#reminders-manual").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Relance « Rappel amical » de la facture F2026-0007 notée comme envoyée."));
        _reminders.Sends.Should().Equal((invoice.InvoiceId, ReminderChannelDto.Manual));
    }

    [Fact]
    public void Un_envoi_refuse_s_affiche_dans_le_toaster_et_l_apercu_reste_ouvert()
    {
        _reminders.Invoices.Add(FakeRemindersGateway.Invoice("F2026-0007", "Dupont SARL", "compta@dupont.fr", 50));
        IRenderedComponent<RemindersPage> page = Open();
        page.Find("#reminders-open-F2026-0007").Click();
        page.WaitForAssertion(() => page.FindAll("#reminders-send").Should().ContainSingle());
        _reminders.FailWith = ErrorCodes.Email.SendFailed;

        page.Find("#reminders-send").Click();

        page.WaitForAssertion(() => this.Toasts().Should().ContainSingle(t => t.Kind == ToastKind.Error));
        page.FindAll("#reminders-preview").Should().ContainSingle();
    }

    [Fact]
    public void Un_apercu_impossible_s_affiche_dans_le_toaster()
    {
        _reminders.Invoices.Add(FakeRemindersGateway.Invoice("F2026-0007", "Dupont SARL", "compta@dupont.fr", 50));
        IRenderedComponent<RemindersPage> page = Open();
        _reminders.FailWith = ErrorCodes.Reminders.CompanyProfileMissing;

        page.Find("#reminders-open-F2026-0007").Click();

        page.WaitForAssertion(() => this.Toasts().Should().ContainSingle(t => t.Kind == ToastKind.Error));
        page.FindAll("#reminders-preview").Should().BeEmpty();
    }

    [Fact]
    public void La_passe_du_jour_dit_combien_sont_parties_et_combien_ont_echoue()
    {
        _reminders.Invoices.Add(FakeRemindersGateway.Invoice("F2026-0007", "Dupont SARL", "compta@dupont.fr", 50));
        IRenderedComponent<RemindersPage> page = Open();

        page.Find("#reminders-run").Click();

        page.WaitForAssertion(() => this.Toasts().Should().ContainSingle(t => t.Kind == ToastKind.Warning
            && t.Message == "1 relance(s) envoyée(s), 1 en échec, 0 client(s) sans adresse e-mail."));

        _reminders.RunResult = new RunRemindersResponse(1, 1, 0, 0);
        page.Find("#reminders-run").Click();
        page.WaitForAssertion(() => this.Toasts().Should().Contain(t => t.Kind == ToastKind.Success));

        _reminders.FailWith = ErrorCodes.Email.NotConfigured;
        page.Find("#reminders-run").Click();
        page.WaitForAssertion(() => this.Toasts().Should().Contain(t => t.Kind == ToastKind.Error));
    }

    [Fact]
    public void Le_scenario_se_regle_et_s_enregistre_avec_un_message()
    {
        IRenderedComponent<RemindersPage> page = Open();

        page.Find("#reminders-delay-1").Change("5");
        page.Find("#reminders-delay-4").Change("60");
        page.Find("#reminders-gap").Change("10");
        page.Find("#reminders-auto").Change(true);
        page.Find("#reminders-settings-save").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Scénario de relance enregistré."));
        UpdateReminderSettingsRequest saved = _reminders.SavedSettings.Should().ContainSingle().Subject;
        (saved.AutoSend, saved.MinimumGapDays).Should().Be((true, 10));
        saved.Delays.Should().Equal(5, 15, 30, 60);

        _reminders.FailWith = ErrorCodes.Reminders.DelaysInvalid;
        page.Find("#reminders-settings-save").Click();
        page.WaitForAssertion(() => this.Toasts().Should().Contain(t => t.Kind == ToastKind.Error));
    }

    [Fact]
    public void L_historique_se_filtre_se_trie_et_se_pagine_sur_le_serveur()
    {
        for (int i = 0; i < 30; i++)
        {
            _reminders.History.Add(FakeRemindersGateway.Sent($"F2026-{i:0000}", "Dupont SARL", i % 2 == 0 ? ReminderLevelDto.Friendly : ReminderLevelDto.Reminder,
                i == 1 ? ReminderOutcomeDto.Failed : ReminderOutcomeDto.Sent, i == 2 ? ReminderTriggerDto.Schedule : ReminderTriggerDto.User));
        }

        IRenderedComponent<RemindersPage> page = Open();

        page.FindAll("#reminders-history tbody tr").Should().HaveCount(25);
        page.Markup.Should().Contain("En échec").And.Contain("automatique");
        page.Find("nav.lt-pager").TextContent.Should().Contain("1").And.Contain("2");

        page.FindAll("nav.lt-pager button")[1].Click();
        page.WaitForAssertion(() => page.FindAll("#reminders-history tbody tr").Should().HaveCount(5));
        _reminders.HistoryQueries[^1].Page.Should().Be(2);

        page.Find("#reminders-filter-level").Change("Reminder");
        page.WaitForAssertion(() => _reminders.HistoryQueries[^1].Should().Match<ReminderHistoryQuery>(q => q.Level == ReminderLevelDto.Reminder && q.Page == 1));

        page.Find("#reminders-filter-outcome").Change("Failed");
        page.Find("#reminders-filter-channel").Change("Email");
        page.Find("#reminders-filter-from").Change("2026-10-01");
        page.Find("#reminders-filter-to").Change("2026-10-31");
        page.Find("#reminders-search").Change("dupont");
        page.Find("#reminders-sort").Change("Customer");
        page.Find("#reminders-sort-direction").Click();

        page.WaitForAssertion(() => _reminders.HistoryQueries[^1].Should().Be(new ReminderHistoryQuery(
            null, ReminderLevelDto.Reminder, ReminderOutcomeDto.Failed, ReminderChannelDto.Email, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31),
            "dupont", ReminderSortDto.Customer, false, 1, 25)));
        page.Find("#reminders-sort-direction").GetAttribute("aria-pressed").Should().Be("false");
    }
}
