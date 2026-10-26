using System.Net;
using System.Net.Http.Json;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;

namespace Lettra.Api.Tests;

/// <summary>
/// Banque connectée par l'API réelle (agrégateur simulé) : banques proposées, consentement refusé puis donné, opérations
/// au rapprochement, relecture, déconnexion, droits.
/// </summary>
public sealed class BankConnectionsApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static Uri Api(string path) => new(path, UriKind.Relative);

    private static HttpClient InDossier(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }

    private static async Task<HttpResponseMessage> StartAsync(HttpClient client, string institution = "sim-horizon") =>
        await client.PostAsJsonAsync(
            Api("/api/v1/bank/connections"), new StartBankConnectionRequest { InstitutionId = institution }, ContractsJsonContext.Default.StartBankConnectionRequest);

    private static async Task<HttpResponseMessage> CompleteAsync(HttpClient client, Guid id, string code) =>
        await client.PostAsJsonAsync(
            Api($"/api/v1/bank/connections/{id}/complete"), new CompleteBankConnectionRequest { Code = code }, ContractsJsonContext.Default.CompleteBankConnectionRequest);

    [Fact]
    public async Task Relier_un_compte_amene_ses_operations_au_rapprochement()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        BankConnectionsResponse offer = (await manager.GetFromJsonAsync(Api("/api/v1/bank/connections"), ContractsJsonContext.Default.BankConnectionsResponse))!;
        offer.Should().BeEquivalentTo(new { Available = true, Provider = "Agrégateur simulé" });
        offer.Institutions.Select(i => i.Id).Should().Contain("sim-horizon");

        using HttpResponseMessage unknown = await StartAsync(manager, "inconnue");
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using HttpResponseMessage started = await StartAsync(manager);
        started.StatusCode.Should().Be(HttpStatusCode.OK, await started.Content.ReadAsStringAsync());
        BankConsentRedirectResponse consent = (await started.Content.ReadFromJsonAsync(ContractsJsonContext.Default.BankConsentRedirectResponse))!;
        consent.RedirectUrl.Should().StartWith("bank/simulated-consent?connection=");

        using HttpResponseMessage refused = await CompleteAsync(manager, consent.ConnectionId, BankConnectionRoutes.SimulatedRefusedCode);
        refused.StatusCode.Should().Be(HttpStatusCode.OK, "un refus n'est pas une erreur : aucune erreur dans la console du navigateur");
        (await refused.Content.ReadFromJsonAsync(ContractsJsonContext.Default.BankConnectionCompletedResponse))!.Granted.Should().BeFalse();

        using HttpResponseMessage granted = await CompleteAsync(manager, consent.ConnectionId, BankConnectionRoutes.SimulatedGrantedCode);
        granted.StatusCode.Should().Be(HttpStatusCode.OK, await granted.Content.ReadAsStringAsync());
        BankConnectionCompletedResponse linked = (await granted.Content.ReadFromJsonAsync(ContractsJsonContext.Default.BankConnectionCompletedResponse))!;
        linked.Connection.Status.Should().Be(BankConnectionStatusDto.Active);
        linked.FirstSync.Imported.Should().BeGreaterThan(0, "les frais mensuels des 90 derniers jours");

        ReconciliationResponse reconciliation = (await manager.GetFromJsonAsync(Api("/api/v1/bank/reconciliation"), ContractsJsonContext.Default.ReconciliationResponse))!;
        reconciliation.Imports.Should().Contain(i => i.Format == BankStatementFormatDto.Connection && i.Imported == linked.FirstSync.Imported);

        using HttpResponseMessage synced = await manager.PostAsJsonAsync(
            Api("/api/v1/bank/connections/sync"), new SyncBankConnectionsRequest { ConnectionId = linked.Connection.Id }, ContractsJsonContext.Default.SyncBankConnectionsRequest);
        SyncBankConnectionsResponse again = (await synced.Content.ReadFromJsonAsync(ContractsJsonContext.Default.SyncBankConnectionsResponse))!;
        again.Outcomes.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Imported = 0, Errors = Array.Empty<ErrorDetail>() });

        using HttpResponseMessage revoked = await manager.PostAsync(Api($"/api/v1/bank/connections/{linked.Connection.Id}/revoke"), null);
        (await revoked.Content.ReadFromJsonAsync(ContractsJsonContext.Default.BankConnectionResponse))!.Status.Should().Be(BankConnectionStatusDto.Revoked);
        using HttpResponseMessage twice = await manager.PostAsync(Api($"/api/v1/bank/connections/{linked.Connection.Id}/revoke"), null);
        twice.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using HttpResponseMessage missing = await manager.PostAsync(Api($"/api/v1/bank/connections/{Guid.CreateVersion7()}/revoke"), null);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Un_collaborateur_voit_les_comptes_relies_sans_pouvoir_en_relier()
    {
        using HttpClient collaborator = InDossier(factory.ClientFor(LettraApiFactory.CollaborateurId));

        (await collaborator.GetAsync(Api("/api/v1/bank/connections"))).StatusCode.Should().Be(HttpStatusCode.OK);
        using HttpResponseMessage refused = await StartAsync(collaborator);
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using HttpResponseMessage sync = await collaborator.PostAsJsonAsync(
            Api("/api/v1/bank/connections/sync"), new SyncBankConnectionsRequest { OnlyDue = true }, ContractsJsonContext.Default.SyncBankConnectionsRequest);
        sync.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
