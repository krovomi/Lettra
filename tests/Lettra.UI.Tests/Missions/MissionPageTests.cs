using System.Globalization;
using Bunit;
using Lettra.Contracts.Common;
using Lettra.Contracts.Missions;
using Lettra.Printing;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Missions;
using Lettra.UI.Shared.Pages.Missions;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Billing;
using Lettra.UI.Tests.Printing;
using Lettra.UI.Tests.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Tests.Missions;

/// <summary>« Lettre de mission » : la mission du dossier, sa lettre en PDF, et le temps passé face aux honoraires.</summary>
[Collection("culture")]
public sealed class MissionPageTests : BunitContext
{
    private readonly FakeMissionGateway _missions = new();
    private readonly FakeFileDownloader _files = new();

    public MissionPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IMissionGateway>(_missions);
        Services.AddSingleton<IBillingSettingsGateway>(new FakeBillingSettingsGateway());
        Services.AddSingleton<IFileDownloader>(_files);
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<MissionPage> Open()
    {
        IRenderedComponent<MissionPage> page = Render<MissionPage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    /// <summary>Le texte d'un élément, espaces insécables (milliers, devise) ramenés à des espaces simples.</summary>
    private static string Plain(string text) => text.Replace(' ', ' ').Replace(' ', ' ');

    [Fact]
    public void Sans_mission_le_formulaire_s_ouvre_prerempli_et_l_enregistrement_affiche_le_resume()
    {
        IRenderedComponent<MissionPage> page = Open();

        page.FindAll("#mission-letter").Should().BeEmpty("pas de lettre sans mission");
        page.Find("#mission-service-Bookkeeping").HasAttribute("checked").Should().BeTrue();
        page.Find("#mission-service-Payroll").HasAttribute("checked").Should().BeFalse();
        page.Find("#mission-service-Payroll").Change(true);
        page.Find("#mission-annual-fee").Change("3600");
        page.Find("#mission-schedule").Change("Quarterly");
        page.Find("#mission-rate-target").Change("90");
        page.Find("#mission-signatory").Change("Jeanne Martin");
        page.Find("#mission-form").Submit();

        page.WaitForAssertion(() => page.FindAll("#mission-summary").Should().ContainSingle());
        (SaveMissionRequest request, long? version) = _missions.Saves.Should().ContainSingle().Subject;
        version.Should().BeNull();
        request.StartDate.Should().Be(new DateOnly(2026, 1, 1));
        request.Services.Should().Equal(MissionServiceDto.Bookkeeping, MissionServiceDto.VatReturns, MissionServiceDto.AnnualAccounts,
            MissionServiceDto.TaxReturns, MissionServiceDto.Payroll);
        this.ToastText().Should().Be("Mission enregistrée.");
        Plain(page.Find("#mission-fee").TextContent).Should().Be("3 600,00 €");
        page.Find("#mission-budget").TextContent.Should().Be("Budget : 40 h");
        page.Find("#mission-summary").TextContent.Should().Contain("Jeanne Martin").And.Contain("Pas encore signée");
    }

    [Fact]
    public void Modifier_la_mission_repart_de_la_version_lue()
    {
        _missions.Mission = FakeMissionGateway.Sample();
        IRenderedComponent<MissionPage> page = Open();

        page.Find("#mission-edit").Click();
        page.Find("#mission-signatory").GetAttribute("value").Should().Be("Jeanne Martin");
        page.Find("#mission-annual-fee").Change("4800");
        page.Find("#mission-form").Submit();

        page.WaitForAssertion(() => _missions.Saves.Should().ContainSingle().Which.Version.Should().Be(3));
        _missions.Saves[0].Request.Services.Should().Equal(MissionServiceDto.Bookkeeping, MissionServiceDto.AnnualAccounts);
        Plain(page.Find("#mission-fee").TextContent).Should().Be("4 800,00 €");
    }

    [Fact]
    public void Le_taux_realise_sous_le_taux_vise_se_signale_et_le_budget_depasse_aussi()
    {
        _missions.Mission = FakeMissionGateway.Sample(targetRate: 80m);
        _missions.Earned = 2000m;
        _missions.Entries.AddRange([FakeMissionGateway.Entry(9, 2, 1800, TimeTaskDto.Bookkeeping), FakeMissionGateway.Entry(10, 1, 300, TimeTaskDto.Vat)]);
        IRenderedComponent<MissionPage> page = Open();

        page.Find("#mission-time").TextContent.Should().Be("35 h 00");
        page.Find("#mission-budget").ClassList.Should().Contain("lt-late", "35 h dépassent le budget de 30 h");
        Plain(page.Find("#mission-rate").TextContent).Should().Be("57,14 €/h");
        page.Find("#mission-rate").ParentElement!.ClassList.Should().Contain("is-pending");
        page.FindAll("#mission-by-task li").Select(li => li.TextContent).Should().HaveCount(2);
    }

    [Fact]
    public void Saisir_puis_supprimer_un_temps_rafraichit_la_synthese()
    {
        _missions.Mission = FakeMissionGateway.Sample();
        IRenderedComponent<MissionPage> page = Open();
        page.FindAll("#mission-time-empty").Should().ContainSingle();

        page.Find("#time-hours").Change("1.5");
        page.Find("#time-task").Change("Closing");
        page.Find("#time-note").Change("Révision des stocks");
        page.Find("#mission-time-form").Submit();

        page.WaitForAssertion(() => _missions.Entries.Should().ContainSingle());
        TimeEntryResponse entry = _missions.Entries[0];
        entry.Minutes.Should().Be(90);
        entry.Date.Should().Be(new DateOnly(2026, 10, 26));
        this.ToastText().Should().Be("1 h 30 ajoutées.");
        page.Find("#mission-time").TextContent.Should().Be("1 h 30");
        page.Find("#time-note").GetAttribute("value").Should().BeNullOrEmpty("le formulaire se vide pour la saisie suivante");

        page.Find($"#time-delete-{entry.Id}").Click();
        page.WaitForAssertion(() => _missions.Entries.Should().BeEmpty());
        this.ToastText().Should().Be("Temps supprimé.");
    }

    [Fact]
    public void Les_temps_se_filtrent_et_seuls_les_siens_se_suppriment()
    {
        _missions.Mission = FakeMissionGateway.Sample();
        TimeEntryResponse mine = FakeMissionGateway.Entry(10, 20, 60, TimeTaskDto.Vat, "TVA de septembre");
        TimeEntryResponse other = FakeMissionGateway.Entry(10, 22, 120, TimeTaskDto.Bookkeeping, "Saisie", mine: false, who: "Lucie Bernard");
        _missions.Entries.AddRange([mine, other]);
        IRenderedComponent<MissionPage> page = Open();

        page.FindAll("#mission-entries tbody tr").Select(r => r.Id).Should().Equal($"time-{other.Id}", $"time-{mine.Id}");
        page.FindAll($"#time-delete-{other.Id}").Should().BeEmpty();

        page.Find("#time-filter-task").Change("Vat");
        page.FindAll("#mission-entries tbody tr").Should().ContainSingle().Which.Id.Should().Be($"time-{mine.Id}");

        page.Find("#time-filter-task").Change(string.Empty);
        page.Find("#time-search").Input("lucie");
        page.FindAll("#mission-entries tbody tr").Should().ContainSingle().Which.Id.Should().Be($"time-{other.Id}");

        page.Find("#time-search").Input("introuvable");
        page.FindAll("#time-filtered-empty").Should().ContainSingle();
    }

    [Fact]
    public void La_lettre_de_mission_se_telecharge_en_pdf()
    {
        _missions.Mission = FakeMissionGateway.Sample();
        IRenderedComponent<MissionPage> page = Open();

        page.Find("#mission-letter").Click();

        page.WaitForAssertion(() => _files.Files.Should().ContainSingle());
        _files.Files[0].FileName.Should().Be("lettre-de-mission-2026-10-26.pdf");
        _files.Files[0].MimeType.Should().Be("application/pdf");
        _files.Files[0].Content.Take(4).Should().Equal("%PDF"u8.ToArray());
    }

    [Fact]
    public void La_lettre_ne_se_signe_pas_sans_fiche_anti_blanchiment_a_jour()
    {
        _missions.Mission = FakeMissionGateway.Sample();
        IRenderedComponent<MissionPage> page = Open();

        page.Find("#mission-edit").Click();
        page.Find("#mission-signed").Change("20/10/2026");
        page.Find("#mission-form").Submit();

        _missions.Saves.Should().BeEmpty("refus prévisible : rien ne part au serveur");
        page.Markup.Should().Contain("La lettre ne se signe pas tant que la fiche anti-blanchiment du client n'est pas complète et à jour.");
        page.Find("#mission-vigilance-link").GetAttribute("href").Should().Be("vigilance");

        _missions.ClientKnown = true;
        page.Find("#mission-cancel").Click();
        page.Find("#mission-time-form").Submit();
        page.WaitForAssertion(() => _missions.Entries.Should().ContainSingle());
        page.Find("#mission-edit").Click();
        page.Find("#mission-signed").Change("20/10/2026");
        page.Find("#mission-form").Submit();

        page.WaitForAssertion(() => _missions.Saves.Should().ContainSingle().Which.Request.SignedOn.Should().Be(new DateOnly(2026, 10, 20)));
    }

    [Fact]
    public void Un_refus_du_serveur_s_affiche()
    {
        _missions.Mission = FakeMissionGateway.Sample();
        IRenderedComponent<MissionPage> page = Open();

        _missions.NextError = ErrorCodes.Mission.DurationInvalid;
        page.Find("#mission-time-form").Submit();

        page.WaitForAssertion(() => this.Toasts().Should().ContainSingle().Which.Kind.Should().Be(ToastKind.Error));
        _missions.Entries.Should().BeEmpty();
    }

    [Fact]
    public void La_lettre_reprend_la_mission_le_client_et_les_honoraires()
    {
        IStringLocalizer<SharedResource> localizer = Services.GetRequiredService<IStringLocalizer<SharedResource>>();
        CultureInfo culture = CultureInfo.GetCultureInfo("fr-FR");

        PrintableLetter letter = EngagementLetter.Compose(FakeMissionGateway.Sample(), BillingFixtures.Company(), "Cabinet Exemple", new DateOnly(2026, 10, 26), localizer, culture);
        string text = Plain(PdfDocumentRendererTests.TextOf(LetterPdfRenderer.Build(letter)));

        text.Should().Contain("Lettre de mission : présentation des comptes annuels")
            .And.Contain("Cabinet Martin").And.Contain("12 avenue Foch").And.Contain("75116 Paris")
            .And.Contain("2 400,00 € hors taxes par an").And.Contain("200,00 € hors taxes par facture")
            .And.Contain("Jeanne Martin").And.Contain("Paul Leroy");
        letter.Sections.Should().HaveCount(8);
        letter.Signatures.Select(s => s.Name).Should().Equal("Paul Leroy", "Jeanne Martin");

        PrintableLetter withoutCompany = EngagementLetter.Compose(FakeMissionGateway.Sample() with { Manager = null }, null, null, new DateOnly(2026, 10, 26), localizer, culture);
        withoutCompany.Signatures[0].Name.Should().NotBeNullOrWhiteSpace("sans nom, la lettre signe au nom du cabinet");
    }
}
