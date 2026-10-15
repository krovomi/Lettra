using System.Net;
using System.Net.Http.Json;
using System.Text;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Serialization;

namespace Lettra.Api.Tests;

/// <summary>FEC par l'API réelle (mode 2, SQLite) : validateur intégré et permission d'export.</summary>
public sealed class ExportApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private const string Header =
        "JournalCode\tJournalLib\tEcritureNum\tEcritureDate\tCompteNum\tCompteLib\tCompAuxNum\tCompAuxLib\tPieceRef\tPieceDate\tEcritureLib\tDebit\tCredit\tEcritureLet\tDateLet\tValidDate\tMontantdevise\tIdevise";

    private static Uri Api(string path) => new(path, UriKind.Relative);

    private static HttpClient InDossier(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }

    [Fact]
    public async Task Le_validateur_lit_un_fec_d_un_autre_logiciel()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        string fec = Header + "\r\n"
            + "AC\tAchats\t1\t20260110\t607\tAchats\t\t\tF12\t20260110\tFournisseur fictif\t100,00\t0,00\t\t\t20260110\t\t\r\n"
            + "AC\tAchats\t1\t20260110\t401\tFournisseurs\tF001\tFournisseur fictif\tF12\t20260110\tFournisseur fictif\t0,00\t90,00\t\t\t20260110\t\t\r\n";

        HttpResponseMessage response = await manager.PostAsync(
            Api("/api/v1/exports/fec/validation?fileName=552100554FEC20261231.txt&from=2026-01-01&to=2026-12-31"),
            new ByteArrayContent(Encoding.UTF8.GetBytes(fec)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        FecValidationResponse report = (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.FecValidationResponse))!;
        report.IsValid.Should().BeFalse();
        report.EntryCount.Should().Be(1);
        report.Findings.Select(f => f.Code).Should().Equal("Fec.EntryUnbalanced", "Fec.FileUnbalanced");
    }

    [Fact]
    public async Task Un_exercice_incoherent_est_refuse()
    {
        using HttpClient manager = InDossier(factory.CreateClient());

        HttpResponseMessage response = await manager.GetAsync(Api("/api/v1/exports/fec?from=2026-12-31&to=2026-01-01"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Fec.PeriodInvalid");
    }

    [Fact]
    public async Task L_export_exige_sa_permission()
    {
        using HttpClient collaborator = InDossier(factory.ClientFor(LettraApiFactory.CollaborateurId));

        (await collaborator.GetAsync(Api("/api/v1/exports/fec?from=2026-01-01&to=2026-12-31"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
