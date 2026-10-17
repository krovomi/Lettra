using System.Globalization;
using Bunit;
using Lettra.Contracts.Alerts;
using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests;

[Collection("culture")]
public sealed class AlertsPageTests : BunitContext
{
    private readonly FakeAlertsGateway _gateway = new();

    public AlertsPageTests()
    {
        Services.AddLettraUi();
        Services.AddSingleton<IAlertsGateway>(_gateway);
        UseCulture("fr-FR");
    }

    private static void UseCulture(string name) => LettraCultures.Apply(CultureInfo.GetCultureInfo(name));

    private IRenderedComponent<Alerts> Open(Guid id) => Render<Alerts>(p => p.Add(x => x.Id, id));

    [Fact]
    public void Les_alertes_de_dossier_sont_traduites_sans_donnee_de_dossier()
    {
        _gateway.Add("integrite-chaine", AlertSeverity.S1);
        _gateway.Add("integrite-numerotation", AlertSeverity.S2);

        IRenderedComponent<Alerts> page = Render<Alerts>();

        page.Find("h1").TextContent.Should().Be("Centre d'alertes");
        page.FindAll("tbody tr").Select(r => r.QuerySelector("a")!.TextContent)
            .Should().Equal("Chaîne anti-fraude d'un dossier compromise", "Trou de numérotation dans un dossier");
        page.Find("li.lettra-severity-s1").TextContent.Should().Contain("1 ouverte(s)").And.Contain("non prise en charge depuis");

        UseCulture("en-US");
        page = Render<Alerts>();
        page.Find("h1").TextContent.Should().Be("Alert centre");
        page.Find("tbody a").TextContent.Should().Be("A client file's tamper-evident chain is broken");
    }

    [Fact]
    public void Sans_alerte_le_centre_le_dit() =>
        Render<Alerts>().Markup.Should().Contain("Aucune alerte.");

    [Fact]
    public void Tout_le_cabinet_elargit_la_vue()
    {
        IRenderedComponent<Alerts> page = Render<Alerts>();

        page.Find("#alerts-cabinet").Change(true);
        page.Find("#alerts-closed").Change(true);

        _gateway.Lists.Should().Equal((false, false), (true, false), (true, true));
    }

    [Fact]
    public void Prendre_en_charge_puis_resoudre_trace_la_chronologie()
    {
        Guid id = _gateway.Add("integrite-chaine", AlertSeverity.S1).Alert.Id;
        IRenderedComponent<Alerts> page = Open(id);
        page.Find("#detail-title").TextContent.Should().Be("S1 · Chaîne anti-fraude d'un dossier compromise");

        page.Find("#alert-acknowledge").Click();

        page.FindAll("#alert-acknowledge").Should().BeEmpty();
        page.Find(".lettra-alert-detail dl").TextContent.Should().Contain("Prise en charge par").And.Contain("user-1");
        page.Find("ol.lettra-timeline").TextContent.Should().Contain("Déclenchée").And.Contain("par une règle").And.Contain("par une personne");

        page.Find("#alert-resolve").Click();

        page.FindAll("#alert-resolve").Should().BeEmpty();
        page.Find("tbody tr").TextContent.Should().Contain("Résolue");
    }

    [Fact]
    public void Changer_la_gravite_exige_un_motif()
    {
        Guid id = _gateway.Add("integrite-numerotation", AlertSeverity.S2).Alert.Id;
        IRenderedComponent<Alerts> page = Open(id);

        page.Find("form").Submit();

        page.Markup.Should().Contain("Indiquez un motif.");
        _gateway.LastSeverityChange.Should().BeNull();

        page.Find("#alert-severity").Change(nameof(AlertSeverity.S1));
        page.Find("#alert-reason").Change("Facture validée hors application");
        page.Find("form").Submit();

        _gateway.LastSeverityChange.Should().Be(new ChangeAlertSeverityRequest { Severity = AlertSeverity.S1, Reason = "Facture validée hors application" });
        page.Find("ol.lettra-timeline").TextContent.Should().Contain("Gravité changée : S1").And.Contain("Facture validée hors application");
    }

    [Fact]
    public void Un_refus_de_droit_est_affiche()
    {
        Guid id = _gateway.Add("integrite-chaine", AlertSeverity.S1).Alert.Id;
        _gateway.NextErrors = [new ErrorDetail(ErrorCodes.Security.Forbidden)];
        IRenderedComponent<Alerts> page = Open(id);

        page.Find("#alert-resolve").Click();

        this.ToastText().Should().NotBe(ErrorCodes.Security.Forbidden);
        page.Find("#alert-resolve").Should().NotBeNull();
    }

    [Fact]
    public void Une_alerte_hors_de_portee_n_est_pas_affichee()
    {
        Open(Guid.CreateVersion7());
        this.ToastText()
            .Should().Be("Cette alerte n'existe pas ou n'est pas visible pour vous.");
    }
}
