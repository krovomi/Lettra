using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bunit;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Tax;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Tax;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Billing;
using Lettra.UI.Tests.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Tax;

/// <summary>Kit contrôle fiscal : l'écran ajoute le Factur-X de chaque pièce à l'archive du serveur, puis la télécharge.</summary>
[Collection("culture")]
public sealed class AuditKitPageTests : BunitContext
{
    private static readonly byte[] Xml = Encoding.UTF8.GetBytes("<rsm:CrossIndustryInvoice />");

    private readonly FakeAuditKitGateway _kits = new();
    private readonly FakeInvoicesGateway _invoices = new();
    private readonly FakeFileDownloader _files = new();
    private readonly InvoiceResponse _invoice;

    public AuditKitPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IAuditKitGateway>(_kits);
        Services.AddSingleton<IInvoicesGateway>(_invoices);
        Services.AddSingleton<IBillingSettingsGateway>(new FakeBillingSettingsGateway());
        Services.AddSingleton<IFileDownloader>(_files);
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
        _invoice = _invoices.Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Validated, "FAC-000001",
            new DocumentLineRequest { Designation = "Conseil", UnitCode = "HUR", Quantity = 2m, UnitPriceExclTax = 100m });
    }

    [Fact]
    public void Le_kit_prend_l_annee_en_cours_et_recoit_le_factur_x_de_chaque_piece()
    {
        _kits.Kit = Kit(Piece(_invoice.Id, "FAC-000001"), Piece(Guid.CreateVersion7(), "FAC-000002"));
        IRenderedComponent<AuditKitPage> page = Render<AuditKitPage>();

        page.Find("#audit-kit-prepare").Click();

        page.WaitForElement("#audit-kit-result");
        _kits.Periods.Should().Equal((new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
        (string fileName, string mime, byte[] content) = _files.Files.Should().ContainSingle().Subject;
        fileName.Should().Be("kit.zip");
        mime.Should().Be("application/zip");
        using ZipArchive zip = new(new MemoryStream(content), ZipArchiveMode.Read);
        zip.Entries.Select(e => e.FullName).Should().BeEquivalentTo(
            ["manifeste.json", "factures/FAC-000001-cii.xml", "factures/FAC-000002-cii.xml", "factures/FAC-000001-factur-x.pdf"]);
        using (Stream pdf = zip.GetEntry("factures/FAC-000001-factur-x.pdf")!.Open())
        {
            using MemoryStream bytes = new();
            pdf.CopyTo(bytes);
            Encoding.ASCII.GetString(bytes.ToArray(), 0, 5).Should().Be("%PDF-");
        }

        page.Find("#audit-kit-pdf").TextContent.Should().Be("1");
        page.Find("#audit-kit-pdf-failed").TextContent.Should().Contain("1 facture(s)");
        page.Find("#audit-kit-fec").ClassList.Should().Contain("is-success");
        page.Find("#audit-kit-integrity").TextContent.Should().Contain("intacte");
        page.Find("#audit-kit-fingerprint").TextContent.Should().Be(_kits.ManifestSha256);
        this.ToastText().Should().Be("Kit contrôle fiscal téléchargé.");

        page.Find("#audit-kit-download").Click();
        _files.Files.Should().HaveCount(2, "le même kit se télécharge à nouveau sans repasser par le serveur");
        _kits.Periods.Should().HaveCount(1);
    }

    [Fact]
    public void Un_refus_du_serveur_passe_par_le_toaster_sans_telechargement()
    {
        _kits.Error = ErrorCodes.Fec.SirenMissing;
        IRenderedComponent<AuditKitPage> page = Render<AuditKitPage>();

        page.Find("#audit-kit-prepare").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Contain("SIREN"));
        page.FindAll("#audit-kit-result").Should().BeEmpty();
        _files.Files.Should().BeEmpty();
    }

    [Fact]
    public void Un_kit_avec_des_constats_le_dit()
    {
        _kits.Kit = Kit() with { Integrity = new AuditKitIntegritySummary(false, 12, 1, 3, 2, [1], ["p256-0011223344556677"]) };
        IRenderedComponent<AuditKitPage> page = Render<AuditKitPage>();

        page.Find("#audit-kit-prepare").Click();

        page.WaitForElement("#audit-kit-integrity").ClassList.Should().Contain("is-danger");
        page.Find("#audit-kit-integrity").TextContent.Should().Contain("2 constat(s)");
        page.Find("#audit-kit-closings").TextContent.Should().Be("1");
    }

    [Fact]
    public async Task L_assembleur_refuse_une_archive_sans_manifeste()
    {
        using MemoryStream empty = new();
        using (new ZipArchive(empty, ZipArchiveMode.Create, leaveOpen: true))
        {
        }

        Func<Task> act = () => AuditKitAssembler.AddPdfsAsync(new AuditKitDocument("kit.zip", empty.ToArray()), (_, _, _) => Task.FromResult<byte[]?>([]), default);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    private static AuditKitPieceResponse Piece(Guid id, string number) =>
        new(id, number, InvoiceKindDto.Invoice, new DateOnly(2026, 10, 15), "Dupont SARL", 240m, $"factures/{number}-cii.xml", $"factures/{number}-factur-x.pdf");

    private static AuditKitManifestResponse Kit(params AuditKitPieceResponse[] pieces) => new(
        "lettra-kit-controle-fiscal-v1", Guid.CreateVersion7(), "Conseil Fictif", "552100554", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
        new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero),
        new AuditKitFecSummary("552100554FEC20261231.txt", true, 8, 0, 0, 480m, 480m),
        new AuditKitIntegritySummary(true, 12, 0, 2, 0, [], ["p256-0011223344556677"]),
        pieces, 1, 1, []);

    private sealed class FakeAuditKitGateway : IAuditKitGateway
    {
        public AuditKitManifestResponse Kit { get; set; } = AuditKitPageTests.Kit();

        public string? Error { get; set; }

        public string ManifestSha256 { get; private set; } = string.Empty;

        public List<(DateOnly From, DateOnly To)> Periods { get; } = [];

        public Task<OperationResult<AuditKitDocument>> PrepareAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
        {
            Periods.Add((from, to));
            if (Error is { } code)
            {
                return Task.FromResult(OperationResult<AuditKitDocument>.Failure(code));
            }

            byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(Kit, ContractsJsonContext.Default.AuditKitManifestResponse);
            ManifestSha256 = Convert.ToHexStringLower(SHA256.HashData(manifest));
            using MemoryStream output = new();
            using (ZipArchive zip = new(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                Write(zip, "manifeste.json", manifest);
                foreach (AuditKitPieceResponse piece in Kit.Pieces)
                {
                    Write(zip, piece.XmlFile, Xml);
                }
            }

            return Task.FromResult(OperationResult<AuditKitDocument>.Success(new AuditKitDocument("kit.zip", output.ToArray())));
        }

        private static void Write(ZipArchive zip, string path, byte[] content)
        {
            using Stream stream = zip.CreateEntry(path).Open();
            stream.Write(content);
        }
    }
}
