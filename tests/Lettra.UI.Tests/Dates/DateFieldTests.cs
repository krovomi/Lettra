using System.Globalization;
using Bunit;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Dates;
using Lettra.UI.Shared.Components.Lists;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Exports;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Billing;
using Lettra.UI.Tests.Exports;
using Lettra.UI.Tests.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Dates;

/// <summary>Écran qui lie un champ de date à sa valeur, comme <c>@bind-Value</c> dans une page.</summary>
internal sealed class BoundDate : ComponentBase
{
    [Parameter] public DateOnly? Value { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DateField>(0);
        builder.AddComponentParameter(1, nameof(DateField.Id), "due");
        builder.AddComponentParameter(2, nameof(DateField.Value), Value);
        builder.AddComponentParameter(3, nameof(DateField.ValueChanged), EventCallback.Factory.Create<DateOnly?>(this, v => Value = v));
        builder.CloseComponent();
    }
}

/// <summary>
/// Dates dans la langue de l'écran : « 19/10/2026 » et lundi en premier en français, « 10/19/2026 » et dimanche en premier
/// en anglais, quelle que soit la langue du navigateur (le champ de date natif, lui, suivait le navigateur).
/// </summary>
[Collection("culture")]
public sealed class DateFieldTests : BunitContext
{
    private static readonly DateOnly Today = new(2026, 10, 19);

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    public DateFieldTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(Fr);
    }

    protected override void Dispose(bool disposing)
    {
        LettraCultures.Apply(Fr);
        base.Dispose(disposing);
    }

    [Theory]
    [InlineData("fr-FR", "19/10/2026")]
    [InlineData("en-US", "10/19/2026")]
    public void Une_date_s_ecrit_dans_la_langue_de_l_ecran(string culture, string expected)
    {
        DateText.Format(Today, CultureInfo.GetCultureInfo(culture)).Should().Be(expected);
        DateText.Format(null, CultureInfo.GetCultureInfo(culture)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("fr-FR", "19/10/2026", 2026, 10, 19)]
    [InlineData("fr-FR", "1/2/2026", 2026, 2, 1)]
    [InlineData("fr-FR", "19.10.26", 2026, 10, 19)]
    [InlineData("fr-FR", "19-10-2026", 2026, 10, 19)]
    [InlineData("fr-FR", "19 10 2026", 2026, 10, 19)]
    [InlineData("fr-FR", "19102026", 2026, 10, 19)]
    [InlineData("fr-FR", "191026", 2026, 10, 19)]
    [InlineData("fr-FR", "0103", 2026, 3, 1)]
    [InlineData("fr-FR", "1/3", 2026, 3, 1)]
    [InlineData("fr-FR", "2026-10-19", 2026, 10, 19)]
    [InlineData("fr-FR", "19 octobre 2026", 2026, 10, 19)]
    [InlineData("en-US", "10/19/2026", 2026, 10, 19)]
    [InlineData("en-US", "2/1/2026", 2026, 2, 1)]
    [InlineData("en-US", "10192026", 2026, 10, 19)]
    [InlineData("en-US", "3/1", 2026, 3, 1)]
    [InlineData("en-US", "2026-10-19", 2026, 10, 19)]
    [InlineData("en-US", "October 19, 2026", 2026, 10, 19)]
    public void Une_date_tapee_se_lit_dans_l_ordre_de_la_langue(string culture, string text, int year, int month, int day)
    {
        DateText.TryParse(text, CultureInfo.GetCultureInfo(culture), Today, out DateOnly? date).Should().BeTrue();
        date.Should().Be(new DateOnly(year, month, day));
    }

    [Theory]
    [InlineData("fr-FR", "31/02/2026")]
    [InlineData("fr-FR", "10/19/2026")]
    [InlineData("fr-FR", "abc")]
    [InlineData("fr-FR", "19/10/202")]
    [InlineData("fr-FR", "1910202")]
    [InlineData("fr-FR", "19/10/2026/1")]
    [InlineData("en-US", "19/10/2026")]
    [InlineData("en-US", "0/1/2026")]
    public void Un_texte_qui_n_est_pas_une_date_est_refuse(string culture, string text)
    {
        DateText.TryParse(text, CultureInfo.GetCultureInfo(culture), Today, out DateOnly? date).Should().BeFalse();
        date.Should().BeNull();
    }

    [Fact]
    public void Un_texte_vide_efface_la_date()
    {
        DateText.TryParse("  ", Fr, Today, out DateOnly? date).Should().BeTrue();
        date.Should().BeNull();
    }

    [Fact]
    public void L_ordre_jour_mois_annee_vient_de_la_date_courte_de_la_culture()
    {
        DateText.Order(Fr).Should().Be(new DateText.DateParts(0, 1, 2));
        DateText.Order(En).Should().Be(new DateText.DateParts(1, 0, 2));
        DateText.Order(CultureInfo.GetCultureInfo("ja-JP")).Should().Be(new DateText.DateParts(2, 1, 0));
        DateText.TryParse("20261019", CultureInfo.GetCultureInfo("ja-JP"), Today, out DateOnly? date).Should().BeTrue();
        date.Should().Be(Today);
    }

    [Fact]
    public void En_francais_le_champ_montre_jj_mm_aaaa_et_relit_la_saisie()
    {
        IRenderedComponent<DateField> labelled = Render<DateField>(p => p.Add(f => f.Id, "labelled").AddUnmatched("aria-label", "Échéance"));
        labelled.Find("#labelled").GetAttribute("aria-label").Should().Be("Échéance");

        IRenderedComponent<BoundDate> field = Render<BoundDate>(p => p.Add(f => f.Value, Today));
        AngleSharp.Dom.IElement input = field.Find("#due");
        input.GetAttribute("type").Should().Be("text", "le champ natif suivrait la langue du navigateur");
        input.GetAttribute("value").Should().Be("19/10/2026");
        input.GetAttribute("placeholder").Should().Be("jj/mm/aaaa");
        field.Find("#due-calendar-toggle").GetAttribute("aria-label").Should().Be("Ouvrir le calendrier");

        field.Find("#due").Change("1/11/26");
        field.Instance.Value.Should().Be(new DateOnly(2026, 11, 1));
        field.Find("#due").GetAttribute("value").Should().Be("01/11/2026", "la saisie est remise au format de la langue");
    }

    [Fact]
    public void In_english_the_field_shows_mm_dd_yyyy_and_reads_what_is_typed()
    {
        LettraCultures.Apply(En);
        IRenderedComponent<BoundDate> field = Render<BoundDate>(p => p.Add(f => f.Value, Today));

        field.Find("#due").GetAttribute("value").Should().Be("10/19/2026");
        field.Find("#due").GetAttribute("placeholder").Should().Be("mm/dd/yyyy");
        field.Find("#due-calendar-toggle").GetAttribute("aria-label").Should().Be("Open calendar");

        field.Find("#due").Change("11/1/2026");
        field.Instance.Value.Should().Be(new DateOnly(2026, 11, 1));
        field.Find("#due").GetAttribute("value").Should().Be("11/1/2026");

        field.Find("#due").Change("31/12/2026");
        field.Find("#due-error").TextContent.Should().Be("Date not recognized: type mm/dd/yyyy, for example 10/19/2026.");
    }

    [Fact]
    public void Une_saisie_invalide_reste_affichee_avec_son_erreur_et_ne_change_pas_la_valeur()
    {
        int changes = 0;
        IRenderedComponent<DateField> field = Render<DateField>(p => p
            .Add(f => f.Id, "due").Add(f => f.Value, Today).Add(f => f.ValueChanged, _ => changes++));

        field.Find("#due").Change("32/10/2026");

        changes.Should().Be(0);
        field.Find("#due").GetAttribute("value").Should().Be("32/10/2026");
        field.Find("#due").GetAttribute("aria-invalid").Should().Be("true");
        field.Find("#due").GetAttribute("aria-describedby").Should().Be("due-error");
        field.Find("#due-error").TextContent.Should().Be("Date non reconnue : saisir jj/mm/aaaa, par exemple 19/10/2026.");

        field.Find("#due").Change("20/10/2026");
        changes.Should().Be(1);
        field.FindAll("#due-error").Should().BeEmpty();
        field.Find("#due").HasAttribute("aria-invalid").Should().BeFalse();
    }

    [Fact]
    public void Une_date_hors_des_bornes_est_refusee_avec_les_bornes_dans_la_langue()
    {
        DateOnly? value = null;
        IRenderedComponent<DateField> field = Render<DateField>(p => p
            .Add(f => f.Id, "pay").Add(f => f.ValueChanged, v => value = v)
            .Add(f => f.Min, new DateOnly(2026, 1, 1)).Add(f => f.Max, Today));

        field.Find("#pay").Change("20/10/2026");
        value.Should().BeNull();
        field.Find("#pay-error").TextContent.Should().Be("Choisir une date entre le 01/01/2026 et le 19/10/2026.");

        IRenderedComponent<DateField> after = Render<DateField>(p => p.Add(f => f.Id, "a").Add(f => f.Min, Today));
        after.Find("#a").Change("18/10/2026");
        after.Find("#a-error").TextContent.Should().Be("Choisir une date à partir du 19/10/2026.");

        IRenderedComponent<DateField> before = Render<DateField>(p => p.Add(f => f.Id, "b").Add(f => f.Max, Today));
        before.Find("#b").Change("20/10/2026");
        before.Find("#b-error").TextContent.Should().Be("Choisir une date jusqu'au 19/10/2026.");
    }

    [Fact]
    public void Une_date_obligatoire_effacee_revient_a_la_valeur_gardee()
    {
        IRenderedComponent<FecExport> page = RenderFec();
        page.Find("#fec-from").Change(string.Empty);
        page.Find("#fec-from").GetAttribute("value").Should().Be($"01/01/{DateTime.Today.Year}");
    }

    [Fact]
    public void Le_calendrier_francais_commence_le_lundi_et_nomme_les_mois_en_francais()
    {
        IRenderedComponent<BoundDate> field = Render<BoundDate>(p => p.Add(f => f.Value, Today));

        field.Find("#due-calendar-toggle").Click();

        field.Find("#due-calendar").GetAttribute("role").Should().Be("dialog");
        field.Find("#due-calendar-toggle").GetAttribute("aria-expanded").Should().Be("true");
        field.Find("#due-calendar-month").TextContent.Should().Be("Octobre 2026");
        field.FindAll(".lt-date-grid th")[0].TextContent.Should().Be("lun.");
        field.FindAll(".lt-date-grid th")[6].TextContent.Should().Be("dim.");
        field.FindAll(".lt-date-day").Should().HaveCount(42);
        field.FindAll(".lt-date-day")[0].GetAttribute("data-date").Should().Be("2026-09-28", "octobre 2026 commence un jeudi");
        AngleSharp.Dom.IElement today = field.Find(".lt-date-day.is-today");
        today.GetAttribute("aria-current").Should().Be("date");
        today.GetAttribute("aria-label").Should().Be("lundi 19 octobre 2026");
        today.ClassList.Should().Contain("is-selected");

        field.Find("[data-date='2026-10-23']").Click();

        field.Instance.Value.Should().Be(new DateOnly(2026, 10, 23));
        field.FindAll("#due-calendar").Should().BeEmpty("le calendrier se ferme au choix");
        field.Find("#due").GetAttribute("value").Should().Be("23/10/2026");
    }

    [Fact]
    public void The_english_calendar_starts_on_sunday_and_names_months_in_english()
    {
        LettraCultures.Apply(En);
        IRenderedComponent<DateField> field = Render<DateField>(p => p.Add(f => f.Id, "due").Add(f => f.Value, Today));

        field.Find("#due-calendar-toggle").Click();

        field.Find("#due-calendar-month").TextContent.Should().Be("October 2026");
        field.FindAll(".lt-date-grid th")[0].TextContent.Should().Be("Sun");
        field.FindAll(".lt-date-day")[0].GetAttribute("data-date").Should().Be("2026-09-27");
        field.Find(".lt-date-day.is-today").GetAttribute("aria-label").Should().Be("Monday, October 19, 2026");
        field.Find("#due-calendar-today").TextContent.Should().Be("Today");
    }

    [Fact]
    public void Le_clavier_et_les_boutons_parcourent_le_calendrier()
    {
        DateOnly? value = null;
        IRenderedComponent<DateField> field = Render<DateField>(p => p
            .Add(f => f.Id, "due").Add(f => f.ValueChanged, v => value = v));
        field.Find("#due-calendar-toggle").Click();

        string Focused() => field.Find(".lt-date-day[tabindex='0']").GetAttribute("data-date")!;
        void Press(string key, bool shift = false) => field.Find("#due-calendar").KeyDown(new KeyboardEventArgs { Key = key, ShiftKey = shift });

        Focused().Should().Be("2026-10-19", "sans valeur, le calendrier s'ouvre sur aujourd'hui");
        Press("ArrowRight");
        Focused().Should().Be("2026-10-20");
        Press("ArrowDown");
        Focused().Should().Be("2026-10-27");
        Press("ArrowLeft");
        Press("ArrowUp");
        Focused().Should().Be("2026-10-19");
        Press("End");
        Focused().Should().Be("2026-10-25", "la semaine finit le dimanche en français");
        Press("Home");
        Focused().Should().Be("2026-10-19");
        Press("PageDown");
        field.Find("#due-calendar-month").TextContent.Should().Be("Novembre 2026");
        Press("PageUp");
        Press("PageUp", shift: true);
        field.Find("#due-calendar-month").TextContent.Should().Be("Octobre 2025");
        field.Find("#due-calendar-next").Click();
        field.Find("#due-calendar-previous").Click();
        field.Find("#due-calendar-previous").Click();
        field.Find("#due-calendar-month").TextContent.Should().Be("Septembre 2025");
        Press("Enter");
        field.FindAll("#due-calendar").Should().ContainSingle("une autre touche ne fait rien");

        field.Find("#due-calendar-today").Click();
        value.Should().Be(Today);

        field.Find("#due-calendar-toggle").Click();
        Press("Escape");
        field.FindAll("#due-calendar").Should().BeEmpty();
        field.Find("#due-calendar-toggle").Click();
        field.Find("#due-calendar-close").Click();
        field.FindAll("#due-calendar").Should().BeEmpty();
        field.Find("#due-calendar-toggle").Click();
        field.Find(".lt-date-backdrop").Click();
        field.FindAll("#due-calendar").Should().BeEmpty("un clic à côté ferme le calendrier");
    }

    [Fact]
    public void Le_calendrier_grise_les_jours_hors_des_bornes()
    {
        int picks = 0;
        IRenderedComponent<DateField> field = Render<DateField>(p => p
            .Add(f => f.Id, "pay").Add(f => f.Max, new DateOnly(2026, 10, 10)).Add(f => f.Min, new DateOnly(2026, 10, 5))
            .Add(f => f.ValueChanged, _ => picks++));
        field.Find("#pay-calendar-toggle").Click();

        field.Find(".lt-date-day[tabindex='0']").GetAttribute("data-date").Should().Be("2026-10-10", "aujourd'hui est après la borne");
        field.Find("[data-date='2026-10-11']").HasAttribute("disabled").Should().BeTrue();
        field.Find("[data-date='2026-10-04']").HasAttribute("disabled").Should().BeTrue();
        field.Find("[data-date='2026-10-05']").HasAttribute("disabled").Should().BeFalse();
        field.Find("#pay-calendar-today").HasAttribute("disabled").Should().BeTrue();
        field.Find("#pay-calendar").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        field.Find(".lt-date-day[tabindex='0']").GetAttribute("data-date").Should().Be("2026-10-10", "le parcours s'arrête à la borne");
        picks.Should().Be(0);
    }

    [Fact]
    public void Un_champ_desactive_ou_en_lecture_seule_n_ouvre_pas_de_calendrier()
    {
        IRenderedComponent<DateField> disabled = Render<DateField>(p => p.Add(f => f.Id, "d").Add(f => f.Disabled, true));
        disabled.Find("#d").HasAttribute("disabled").Should().BeTrue();
        disabled.Find("#d-calendar-toggle").HasAttribute("disabled").Should().BeTrue();

        IRenderedComponent<DateField> readOnly = Render<DateField>(p => p.Add(f => f.ReadOnly, true));
        readOnly.Find("input").HasAttribute("readonly").Should().BeTrue();
        readOnly.Find(".lt-date-toggle").HasAttribute("disabled").Should().BeTrue();
        readOnly.Find(".lt-date-toggle").Id.Should().StartWith("date-", "sans id, le champ s'en donne un");
    }

    [Fact]
    public void Les_filtres_de_dates_des_listes_suivent_la_langue()
    {
        LettraCultures.Apply(En);
        IRenderedComponent<DateRangeFilter> filter = Render<DateRangeFilter>(p => p
            .Add(d => d.Id, "dates").Add(d => d.Label, "Issue date").Add(d => d.From, new DateOnly(2026, 1, 31)));

        filter.Find("#dates-from").GetAttribute("value").Should().Be("1/31/2026");
        filter.Find("#dates-to").GetAttribute("placeholder").Should().Be("mm/dd/yyyy");
        filter.Find("#dates-from").GetAttribute("aria-label").Should().Be("Issue date, from");
    }

    [Theory]
    [InlineData("fr-FR", "01/01/{0}", "31/12/{0}")]
    [InlineData("en-US", "1/1/{0}", "12/31/{0}")]
    public void L_export_FEC_affiche_sa_periode_dans_la_langue_de_l_ecran(string culture, string from, string to)
    {
        LettraCultures.Apply(CultureInfo.GetCultureInfo(culture));
        IRenderedComponent<FecExport> page = RenderFec();
        int year = DateTime.Today.Year;

        page.Find("#fec-from").GetAttribute("value").Should().Be(string.Format(CultureInfo.InvariantCulture, from, year));
        page.Find("#fec-to").GetAttribute("value").Should().Be(string.Format(CultureInfo.InvariantCulture, to, year));
    }

    private IRenderedComponent<FecExport> RenderFec()
    {
        Services.AddSingleton<IFecGateway>(new FakeFecGateway());
        Services.AddSingleton<IFileDownloader>(new FakeFileDownloader());
        return Render<FecExport>();
    }
}
