using System.Globalization;
using Bunit;
using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Reminders;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Reminders;
using Lettra.UI.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Reminders;

/// <summary>Avant le retard sur l'écran des relances : rappels avant échéance, échéanciers, opposition au ciblage et réglages.</summary>
[Collection("culture")]
public sealed class AvantLeRetardPageTests : BunitContext
{
    private readonly FakeRemindersGateway _reminders = new();

    public AvantLeRetardPageTests()
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
    public void Les_rappels_avant_echeance_montrent_la_meteo_le_meilleur_jour_et_le_jour_prevu()
    {
        _reminders.Upcoming.Add(FakeRemindersGateway.Coming("F2026-0101", "Dupont SARL", bestDay: DayOfWeek.Tuesday));
        _reminders.Upcoming.Add(FakeRemindersGateway.Coming("F2026-0102", "Hôtel Lefèvre", PaymentWeatherDto.Stormy, dueToday: false,
            sentAt: new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero)));

        IRenderedComponent<RemindersPage> page = Open();

        page.Find("#reminders-upcoming-title").TextContent.Should().Contain("Rappels avant échéance");
        page.FindAll("#reminders-upcoming-table tbody tr").Should().HaveCount(2);
        page.Find("#reminders-upcoming-table").ClassList.Should().Contain("lt-cards");
        page.Find("#reminders-upcoming").TextContent.Should().Contain("Pluvieux").And.Contain("meilleur jour : mardi").And.Contain("Orageux").And.Contain("Parti le 16/10/2026");
        page.FindAll("#reminders-upcoming-open-F2026-0102").Should().BeEmpty("un rappel parti ne se renvoie pas");
        page.Find("#reminders-upcoming-count").TextContent.Should().Be("2 facture(s) sur 2");
    }

    [Fact]
    public void Les_rappels_avant_echeance_se_filtrent_et_se_trient()
    {
        _reminders.Upcoming.Add(FakeRemindersGateway.Coming("F2026-0101", "Dupont SARL"));
        _reminders.Upcoming.Add(FakeRemindersGateway.Coming("F2026-0102", "Hôtel Lefèvre", PaymentWeatherDto.Stormy, dueToday: false));
        _reminders.Upcoming.Add(FakeRemindersGateway.Coming("F2026-0103", "Atelier Martin", dueToday: false));
        IRenderedComponent<RemindersPage> page = Open();

        page.Find("#reminders-upcoming-search").Input("hotel");
        page.Find("#reminders-upcoming-count").TextContent.Should().Be("1 facture(s) sur 3");
        page.Find("#reminders-upcoming-filters-reset").Click();
        page.Find("#reminders-upcoming-weather").Change("Rainy");
        page.Find("#reminders-upcoming-due").Change(true);
        page.FindAll("#reminders-upcoming-table tbody tr a").Select(a => a.TextContent).Should().Equal("F2026-0101");
        page.Find("#reminders-upcoming-due").Change(false);
        page.Find("#reminders-upcoming-sort-customer").Click();
        page.FindAll("#reminders-upcoming-table tbody tr a").Select(a => a.TextContent).Should().Equal("F2026-0103", "F2026-0101");
        page.Find("#reminders-upcoming-weather").Change("Stormy");
        page.Find("#reminders-upcoming-search").Input("zzz");
        page.Find("#reminders-upcoming-nomatch").TextContent.Should().Be("Aucune facture ne correspond à ces filtres.");
    }

    [Fact]
    public void Le_rappel_avant_echeance_s_ouvre_comme_une_relance_puis_part()
    {
        UpcomingReminderResponse coming = FakeRemindersGateway.Coming("F2026-0101", "Dupont SARL");
        _reminders.Upcoming.Add(coming);
        IRenderedComponent<RemindersPage> page = Open();

        page.Find("#reminders-upcoming-open-F2026-0101").Click();
        page.WaitForAssertion(() => page.Find("#reminders-preview-subject").TextContent.Should().Be("Petit rappel : facture F2026-0101 à régler bientôt"));
        page.Find("#reminders-send").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Relance « Rappel avant échéance » de la facture F2026-0101 envoyée à compta@client.fr."));
        _reminders.Sends.Should().Equal((coming.InvoiceId, ReminderChannelDto.Email));
        page.WaitForAssertion(() => page.Find("#reminders-upcoming").TextContent.Should().Contain("Parti le"));
    }

    [Fact]
    public void Ne_pas_cibler_un_client_le_retire_des_rappels_et_l_opposition_se_retire()
    {
        UpcomingReminderResponse coming = FakeRemindersGateway.Coming("F2026-0101", "Dupont SARL");
        _reminders.Upcoming.Add(coming);
        IRenderedComponent<RemindersPage> page = Open();
        page.Find("#reminders-objections-empty").TextContent.Should().Be("Aucun client ne s'est opposé au ciblage.");

        page.Find("#reminders-upcoming-object-F2026-0101").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Dupont SARL ne recevra plus de rappel avant échéance."));
        _reminders.ObjectionChanges.Should().Equal((coming.CustomerId, true));
        page.WaitForAssertion(() => page.FindAll("#reminders-upcoming-empty").Should().ContainSingle());
        page.Find("#reminders-objections").TextContent.Should().Contain("Dupont SARL").And.Contain("depuis le 19/10/2026");

        page.Find($"#reminders-objection-remove-{coming.CustomerId}").Click();

        page.WaitForAssertion(() => this.Toasts().Should().Contain(t => t.Message == "Opposition de Dupont SARL retirée."));
        page.WaitForAssertion(() => page.FindAll("#reminders-objections-empty").Should().ContainSingle());
    }

    [Fact]
    public void Une_opposition_refusee_s_affiche_dans_le_toaster()
    {
        _reminders.Upcoming.Add(FakeRemindersGateway.Coming("F2026-0101", "Dupont SARL"));
        IRenderedComponent<RemindersPage> page = Open();
        _reminders.FailWith = ErrorCodes.Customer.NotFound;

        page.Find("#reminders-upcoming-object-F2026-0101").Click();

        page.WaitForAssertion(() => this.Toasts().Should().ContainSingle(t => t.Kind == ToastKind.Error));
    }

    [Fact]
    public void Sans_rappel_avant_echeance_l_ecran_dit_ou_l_activer()
    {
        _reminders.Settings = _reminders.Settings with { Prevention = new ReminderPreventionDto(false, 3, false, 3, 300m) };

        IRenderedComponent<RemindersPage> page = Open();

        page.Find("#reminders-upcoming-off").TextContent.Should().Contain("activez-le dans le scénario");
        page.Find("#reminders-plans").TextContent.Should().Contain("L'échéancier en libre-service est coupé");
        page.Find("#reminders-plans-empty").TextContent.Should().Contain("Aucun échéancier");
    }

    [Fact]
    public void Les_echeanciers_montrent_leurs_echeances_et_ceux_a_voir()
    {
        _reminders.Plans.Add(FakeRemindersGateway.Plan("F2026-0042", "Dupont SARL", PaymentPlanStatusDto.Late));
        _reminders.Plans.Add(FakeRemindersGateway.Plan("F2026-0043", "Hôtel Lefèvre", PaymentPlanStatusDto.Completed, acknowledged: true));

        IRenderedComponent<RemindersPage> page = Open();

        page.Find("#reminders-plans-waiting").TextContent.Should().Be("1 à voir");
        page.FindAll("#reminders-plans-table tbody tr").Should().HaveCount(2);
        page.Find("#reminders-plans-table").TextContent.Should().Contain("Échéance manquée").And.Contain("Soldé").And.Contain("Nouveau")
            .And.Contain("15/10/2026").And.Contain("réglée").And.Contain("manquée").And.Contain("Jean Durand");
        page.FindAll("#reminders-plan-F2026-0043-ack").Should().BeEmpty("un échéancier vu n'a plus de « Vu »");
        page.FindAll("#reminders-plan-F2026-0043-cancel").Should().BeEmpty("un échéancier soldé ne s'arrête plus");

        page.Find("#reminders-plans-waiting-only").Change(true);
        page.Find("#reminders-plans-count").TextContent.Should().Be("1 échéancier(s) sur 2");
        page.Find("#reminders-plans-waiting-only").Change(false);
        page.Find("#reminders-plans-status").Change("Completed");
        page.FindAll("#reminders-plans-table tbody tr a").Select(a => a.TextContent).Should().Equal("F2026-0043");
        page.Find("#reminders-plans-filters-reset").Click();
        page.Find("#reminders-plans-search").Input("lefevre");
        page.FindAll("#reminders-plans-table tbody tr a").Select(a => a.TextContent).Should().Equal("F2026-0043");
        page.Find("#reminders-plans-search").Input("zzz");
        page.Find("#reminders-plans-nomatch").TextContent.Should().Be("Aucune facture ne correspond à ces filtres.");
        page.Find("#reminders-plans-filters-reset").Click();
        page.Find("#reminders-plans-sort-number").Click();
        page.FindAll("#reminders-plans-table tbody tr a").Select(a => a.TextContent).Should().Equal("F2026-0042", "F2026-0043");
    }

    [Fact]
    public void Prendre_en_compte_puis_arreter_un_echeancier_apres_confirmation()
    {
        PaymentPlanResponse plan = FakeRemindersGateway.Plan("F2026-0042", "Dupont SARL");
        _reminders.Plans.Add(plan);
        IRenderedComponent<RemindersPage> page = Open();

        page.Find("#reminders-plan-F2026-0042-ack").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Échéancier de la facture F2026-0042 pris en compte."));
        page.WaitForAssertion(() => page.FindAll("#reminders-plans-waiting").Should().BeEmpty());
        _reminders.Decisions.Should().Equal((plan.Id, "acknowledge", 1L));

        page.Find("#reminders-plan-F2026-0042-cancel").Click();
        page.Find("#reminders-plan-F2026-0042-cancel-back").Click();
        _reminders.Decisions.Should().HaveCount(1, "« Garder » n'arrête rien");
        page.Find("#reminders-plan-F2026-0042-cancel").Click();
        page.Find("#reminders-plan-F2026-0042-cancel-confirm").Click();

        page.WaitForAssertion(() => this.Toasts().Should().Contain(t => t.Message == "Échéancier de la facture F2026-0042 arrêté : la facture reprend ses relances."));
        _reminders.Decisions[^1].Should().Be((plan.Id, "cancel", 2L));
        page.WaitForAssertion(() => page.Find("#reminders-plans-table").TextContent.Should().Contain("Arrêté"));
        page.FindAll("#reminders-plan-F2026-0042-cancel").Should().BeEmpty();
    }

    [Fact]
    public void Une_decision_refusee_s_affiche_dans_le_toaster()
    {
        _reminders.Plans.Add(FakeRemindersGateway.Plan("F2026-0042", "Dupont SARL"));
        IRenderedComponent<RemindersPage> page = Open();
        _reminders.FailWith = ErrorCodes.Concurrency.VersionMismatch;

        page.Find("#reminders-plan-F2026-0042-ack").Click();

        page.WaitForAssertion(() => this.Toasts().Should().ContainSingle(t => t.Kind == ToastKind.Error));
    }

    [Fact]
    public void Le_rappel_avant_echeance_et_les_bornes_de_l_echeancier_s_enregistrent_avec_le_scenario()
    {
        IRenderedComponent<RemindersPage> page = Open();

        page.Find("#reminders-before-due-days").Change("5");
        page.Find("#reminders-plans-max").Change("2");
        page.Find("#reminders-plans-min").Change("150");
        page.Find("#reminders-before-due").Change(false);
        page.Find("#reminders-before-due-days").HasAttribute("disabled").Should().BeTrue();
        page.Find("#reminders-settings-save").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Scénario de relance enregistré."));
        _reminders.SavedSettings.Single().Prevention.Should().Be(new ReminderPreventionDto(false, 5, true, 2, 150m));

        page.Find("#reminders-plans-enabled").Change(false);
        page.Find("#reminders-plans-max").HasAttribute("disabled").Should().BeTrue();
        page.Find("#reminders-plans-min").HasAttribute("disabled").Should().BeTrue();
    }
}
