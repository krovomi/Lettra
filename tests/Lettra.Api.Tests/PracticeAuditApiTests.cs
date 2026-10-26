using System.Net;
using System.Net.Http.Json;
using System.Text;
using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Serialization;
using Lettra.Domain.SharedKernel;

namespace Lettra.Api.Tests;

/// <summary>
/// Contrôle fiscal d'entraînement par l'API réelle (mode 2, SQLite) : sur le FEC du dossier ou sur celui d'un autre
/// logiciel, avec la permission du FEC.
/// </summary>
public sealed class PracticeAuditApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private const string OtherSoftware =
        "JournalCode\tJournalLib\tEcritureNum\tEcritureDate\tCompteNum\tCompteLib\tCompAuxNum\tCompAuxLib\tPieceRef\tPieceDate\tEcritureLib\tDebit\tCredit\tEcritureLet\tDateLet\tValidDate\tMontantdevise\tIdevise\r\n"
        + "CA\tCaisse\t1\t20260104\t607\tAchats\t\t\tT1\t20260104\tAchat comptant\t1500,00\t0,00\t\t\t20260104\t\t\r\n"
        + "CA\tCaisse\t1\t20260104\t530\tCaisse\t\t\tT1\t20260104\tAchat comptant\t0,00\t1500,00\t\t\t20260104\t\t\r\n";

    private static Uri Api(string path) => new(path, UriKind.Relative);

    private static HttpClient InDossier(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }

    [Fact]
    public async Task Le_fec_du_dossier_passe_les_tests_du_verificateur()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        await ExportApiTests.ValidatedInvoiceAsync(manager);
        int year = LegalCalendar.DateOf(DateTimeOffset.UtcNow).Year;

        HttpResponseMessage response = await manager.GetAsync(Api($"/api/v1/insights/practice-audit?from={year}-01-01&to={year}-12-31"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        PracticeAuditResponse audit = (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.PracticeAuditResponse))!;
        audit.Readable.Should().BeTrue();
        audit.Entries.Should().BePositive();
        audit.Checks.Should().HaveCount(13);
        audit.Checks.Single(c => c.Code == "Format").Severity.Should().NotBe(PracticeAuditSeverityDto.Alert, "Lettra produit un FEC conforme");
        audit.Checks.Single(c => c.Code == "VatRate").Severity.Should().Be(PracticeAuditSeverityDto.Passed);
    }

    [Fact]
    public async Task Le_fec_d_un_autre_logiciel_montre_ce_qu_un_verificateur_releverait()
    {
        using HttpClient manager = InDossier(factory.CreateClient());

        HttpResponseMessage response = await manager.PostAsync(
            Api("/api/v1/insights/practice-audit?fileName=552100554FEC20261231.txt&from=2026-01-01&to=2026-12-31"),
            new ByteArrayContent(Encoding.UTF8.GetBytes(OtherSoftware)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        PracticeAuditResponse audit = (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.PracticeAuditResponse))!;
        audit.FileName.Should().Be("552100554FEC20261231.txt");
        audit.Checks.Single(c => c.Code == "CashOverLimit").Severity.Should().Be(PracticeAuditSeverityDto.Alert);
        audit.Checks.Single(c => c.Code == "NegativeCash").Examples.Should().Equal("530 · 2026-01-04 · -1500.00");
        audit.Checks.Single(c => c.Code == "SundayOrHoliday").Count.Should().Be(1, "le 4 janvier 2026 est un dimanche");
    }

    [Fact]
    public async Task Un_exercice_incoherent_est_refuse()
    {
        using HttpClient manager = InDossier(factory.CreateClient());

        HttpResponseMessage response = await manager.GetAsync(Api("/api/v1/insights/practice-audit?from=2026-12-31&to=2026-01-01"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Fec.PeriodInvalid);
    }

    [Fact]
    public async Task Le_controle_exige_la_permission_du_fec()
    {
        using HttpClient collaborator = InDossier(factory.ClientFor(LettraApiFactory.CollaborateurId));

        (await collaborator.GetAsync(Api("/api/v1/insights/practice-audit?from=2026-01-01&to=2026-12-31"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await collaborator.PostAsync(Api("/api/v1/insights/practice-audit?fileName=FEC.txt"), new ByteArrayContent([1]))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
