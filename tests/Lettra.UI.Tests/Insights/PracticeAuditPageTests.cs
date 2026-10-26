using System.Globalization;
using Bunit;
using Lettra.Contracts.Insights;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Tax;
using Lettra.UI.Shared.Services;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Insights;

/// <summary>Écran du contrôle fiscal d'entraînement : verdict, tests du plus grave au plus anodin, loi de Benford.</summary>
[Collection("culture")]
public sealed class PracticeAuditPageTests : BunitContext
{
    private readonly FakeInsightsGateway _insights = new();

    public PracticeAuditPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IInsightsGateway>(_insights);
        Services.AddSingleton<TimeProvider>(new Security.ManualClock(new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private static PracticeAuditCheckResponse Passed(string code, decimal? value = null) => new(code, PracticeAuditSeverityDto.Passed, 0, value, []);

    private static PracticeAuditResponse Report(bool readable = true, params PracticeAuditCheckResponse[] checks) =>
        new(
            "552100554FEC20261231.txt",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            12,
            40,
            readable,
            checks,
            readable ? [.. Enumerable.Range(1, 9).Select(d => new BenfordDigitResponse(d, d == 1 ? 60m : 5m, 11.1m))] : []);

    [Fact]
    public void Le_controle_du_dossier_donne_le_verdict_puis_chaque_test_avec_ses_exemples_et_sa_reponse()
    {
        _insights.Audit = Report(
            true,
            new PracticeAuditCheckResponse("CashOverLimit", PracticeAuditSeverityDto.Alert, 2, 2_700m, ["CA 2 · 1200.00", "CA 5 · 1500.00"]),
            new PracticeAuditCheckResponse("RoundAmounts", PracticeAuditSeverityDto.Watch, 3, 37.5m, ["VE 1 · 1200.00"]),
            Passed("Format"),
            Passed("VatRate", 20m));
        IRenderedComponent<PracticeAuditPage> page = Render<PracticeAuditPage>();

        page.Find("#audit-run").Click();

        _insights.Runs.Should().Equal((string.Empty, 0, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
        page.Find("#audit-verdict").TextContent.Should().Contain("1 point(s) que le vérificateur relèverait, 1 question(s)");
        page.Find("#audit-verdict").ClassList.Should().Contain("is-danger");
        page.Find("#audit-read").TextContent.Should().Be("12 écriture(s) et 40 ligne(s) lues.");
        page.FindAll("#audit-checks > li").Select(l => l.Id).Should().Equal("audit-CashOverLimit", "audit-RoundAmounts", "audit-Format", "audit-VatRate");
        page.Find("#audit-CashOverLimit").ClassList.Should().Contain("is-alert");
        page.Find("#audit-CashOverLimit").TextContent.Should().Contain("Espèces au-delà de 1 000 €").And.Contain("700,00").And.Contain("CA 5 · 1500.00").And.Contain("dépôt en banque");
        page.Find("#audit-RoundAmounts").TextContent.Should().Contain("37,5 %");
        page.Find("#audit-Format").TextContent.Should().Contain("Le fichier est conforme.").And.NotContain("Corrigez", "un test réussi ne donne pas de consigne");
        page.FindAll("#audit-benford li").Should().HaveCount(9);
        page.Find("#audit-benford .is-observed").GetAttribute("style").Should().Be("width:100%", "60 % observés remplissent la barre");
        page.FindAll("#audit-benford .is-expected")[0].GetAttribute("style").Should().Be("width:22.2%", "le style CSS ne dépend pas de la langue");
        page.Find("#audit-disclaimer").Should().NotBeNull();
    }

    [Fact]
    public void Un_fec_sans_anomalie_et_un_fichier_illisible_ont_chacun_leur_verdict()
    {
        _insights.Audit = Report(true, Passed("Format"), Passed("Benford") with { Count = 12 });
        IRenderedComponent<PracticeAuditPage> page = Render<PracticeAuditPage>();
        page.Find("#audit-run").Click();
        page.Find("#audit-verdict").TextContent.Should().Contain("Aucun des 2 tests");
        page.Find("#audit-verdict").ClassList.Should().Contain("is-success");
        page.Find("#audit-Benford").TextContent.Should().Contain("Seulement 12 montant(s)", "trop peu de montants : le test ne conclut pas");

        _insights.Audit = Report(false, new PracticeAuditCheckResponse("Format", PracticeAuditSeverityDto.Alert, 1, null, ["Fec.HeaderInvalid · L1"]));
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("Bonjour", "notes.txt"));
        page.WaitForAssertion(() => page.Find("#audit-verdict").TextContent.Should().Contain("n'est pas un FEC lisible"));
        page.Find("#audit-Format code").TextContent.Should().NotContain("Fec.HeaderInvalid", "le code du validateur est traduit").And.EndWith(" · L1");
        _insights.Runs[^1].Should().Be(("notes.txt", 7, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
        page.FindAll("#audit-benford").Should().BeEmpty();
    }

    [Fact]
    public void Un_refus_s_affiche_et_l_ecran_existe_en_anglais()
    {
        _insights.Audit = null;
        IRenderedComponent<PracticeAuditPage> page = Render<PracticeAuditPage>();
        page.Find("#audit-run").Click();
        page.FindAll("#audit-result").Should().BeEmpty();
        this.Toasts().Should().ContainSingle().Which.Kind.Should().Be(ToastKind.Error);

        LettraCultures.Apply(CultureInfo.GetCultureInfo("en-US"));
        _insights.Audit = Report(true, Passed("Format"));
        page = Render<PracticeAuditPage>();
        page.Find("h1").TextContent.Should().Be("Practice tax audit");
        page.Find("#audit-run").Click();
        page.Find("#audit-verdict").TextContent.Should().Contain("None of the 1 tests");
    }
}
