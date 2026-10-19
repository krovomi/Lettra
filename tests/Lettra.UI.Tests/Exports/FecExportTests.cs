using System.Globalization;
using System.Text;
using Bunit;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Exports;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Billing;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Exports;

/// <summary>Passerelle FEC en mémoire : export d'un fichier fixe, rapport à la demande, refus de période comme le serveur.</summary>
internal sealed class FakeFecGateway : IFecGateway
{
    public const string FileName = "552100554FEC20261231.txt";

    public static readonly byte[] Exported = Encoding.Latin1.GetBytes("JournalCode\tJournalLib\r\n");

    public string? ExportError { get; set; }

    public FecValidationResponse Report { get; set; } = new(true, FileName, 4, 1, 360m, 360m, [], false);

    public List<(DateOnly From, DateOnly To)> Exports { get; } = [];

    public List<(string FileName, byte[] Content, DateOnly? From, DateOnly? To)> Validations { get; } = [];

    public Task<OperationResult<FecDocument>> ExportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        Exports.Add((from, to));
        return Task.FromResult(
            to < from ? OperationResult<FecDocument>.Failure([new ErrorDetail(ErrorCodes.Fec.PeriodInvalid, "To")])
            : ExportError is { } code ? OperationResult<FecDocument>.Failure(code)
            : OperationResult<FecDocument>.Success(new FecDocument(FileName, Exported)));
    }

    public Task<OperationResult<FecValidationResponse>> ValidateAsync(
        string fileName, byte[] content, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        Validations.Add((fileName, content, from, to));
        return Task.FromResult(OperationResult<FecValidationResponse>.Success(Report with { FileName = fileName }));
    }
}

[Collection("culture")]
public sealed class FecExportTests : BunitContext
{
    private readonly FakeFecGateway _fec = new();
    private readonly FakeFileDownloader _files = new();

    public FecExportTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IFecGateway>(_fec);
        Services.AddSingleton<IFileDownloader>(_files);
        UseCulture("fr-FR");
    }

    private static void UseCulture(string name) => LettraCultures.Apply(CultureInfo.GetCultureInfo(name));

    [Fact]
    public void L_export_remet_le_fichier_puis_affiche_le_rapport_du_validateur()
    {
        IRenderedComponent<FecExport> page = Render<FecExport>();
        page.Find("#fec-from").Change("2026-01-01");
        page.Find("#fec-to").Change("2026-12-31");

        page.Find("#fec-export").Click();

        _fec.Exports.Should().Equal((new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
        _files.Files.Should().ContainSingle().Which.Should().Be((FakeFecGateway.FileName, "text/plain", FakeFecGateway.Exported));
        _fec.Validations.Should().ContainSingle()
            .Which.Should().Be((FakeFecGateway.FileName, FakeFecGateway.Exported, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), "le rapport porte sur le fichier remis");
        page.Find("#fec-exported").TextContent.Should().Be("Fichier 552100554FEC20261231.txt enregistré.");
        page.Find("#fec-verdict").TextContent.Should().Be("Fichier conforme.");
        page.Find("#fec-verdict").ClassList.Should().Contain("is-success");
        page.Find("#fec-debit").TextContent.Should().Be("360,00");
        page.FindAll("#fec-findings").Should().BeEmpty();
    }

    [Fact]
    public void L_exercice_par_defaut_est_l_annee_civile_de_l_horloge_injectee()
    {
        // Une année que l'horloge du poste ne donne pas : l'écran lit bien l'horloge injectée.
        Services.AddSingleton<TimeProvider>(new Security.ManualClock(new DateTimeOffset(2031, 6, 15, 10, 0, 0, TimeSpan.Zero)));
        IRenderedComponent<FecExport> page = Render<FecExport>();

        page.Find("#fec-export").Click();

        _fec.Exports.Should().Equal((new DateOnly(2031, 1, 1), new DateOnly(2031, 12, 31)));
    }

    [Fact]
    public void Un_exercice_incoherent_est_refuse_sans_rien_telecharger()
    {
        IRenderedComponent<FecExport> page = Render<FecExport>();
        page.Find("#fec-from").Change("2026-12-31");
        page.Find("#fec-to").Change("2026-01-01");

        page.Find("#fec-export").Click();

        this.ToastText().Should().Be("La fin de l'exercice précède son début.");
        _files.Files.Should().BeEmpty();
        page.FindAll("#fec-report").Should().BeEmpty();
    }

    [Fact]
    public void Sans_siren_l_ecran_dit_quoi_renseigner()
    {
        _fec.ExportError = ErrorCodes.Fec.SirenMissing;
        IRenderedComponent<FecExport> page = Render<FecExport>();

        page.Find("#fec-export").Click();

        this.ToastText().Should().Be("Renseignez le SIREN de l'entreprise : il compose le nom du fichier FEC.");
        _files.Files.Should().BeEmpty();
        _fec.Validations.Should().BeEmpty();
    }

    [Fact]
    public void Le_fec_d_un_autre_logiciel_est_controle_et_ses_constats_traduits()
    {
        _fec.Report = new FecValidationResponse(
            false, "ignoré", 3, 1, 100m, 90m,
            [
                new FecFindingResponse(ErrorCodes.Fec.EntryUnbalanced, FecFindingSeverityDto.Error, 2, null, "1"),
                new FecFindingResponse(ErrorCodes.Fec.ZeroLine, FecFindingSeverityDto.Warning, 3, "Debit", "1"),
            ],
            FindingsTruncated: true);
        IRenderedComponent<FecExport> page = Render<FecExport>();
        page.Find("#fec-from").Change("2026-01-01");
        page.Find("#fec-to").Change("2026-12-31");

        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("AC\tAchats", "export-autre-logiciel.txt"));

        page.WaitForAssertion(() => page.Find("#fec-verdict").TextContent.Should().Be("Fichier non conforme : 1 erreur(s) à corriger."));
        _fec.Validations.Should().ContainSingle().Which.FileName.Should().Be("export-autre-logiciel.txt");
        _fec.Validations[0].From.Should().Be(new DateOnly(2026, 1, 1));
        Encoding.UTF8.GetString(_fec.Validations[0].Content).Should().Be("AC\tAchats");
        page.FindAll("#fec-exported").Should().BeEmpty("aucun export dans ce cas");
        page.FindAll("#fec-findings tbody tr").Select(r => r.TextContent)
            .Should().SatisfyRespectively(
                error => error.Should().Contain("Erreur").And.Contain("L'écriture n'est pas équilibrée"),
                warning => warning.Should().Contain("À vérifier").And.Contain("Debit").And.Contain("ni débit ni crédit"));
        page.Find("#fec-truncated").Should().NotBeNull();

        UseCulture("en-US");
        page = Render<FecExport>();
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("AC", "other.txt"));
        page.WaitForAssertion(() => page.Find("#fec-verdict").TextContent.Should().Be("The file is not compliant: 1 error(s) to fix."));
        page.Find("h1").TextContent.Should().Be("Accounting entries file (FEC)");
    }
}
