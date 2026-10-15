using System.Net;
using System.Net.Http.Json;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Alerts;
using Lettra.Contracts.Common;
using Lettra.Contracts.Integrity;
using Lettra.Contracts.Serialization;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Integrity;
using Lettra.Supervision.Application;
using Lettra.Supervision.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Sortie J2 de la chaîne anti-fraude, par l'API réelle (mode 2, SQLite) : une ligne du journal modifiée directement
/// en base est détectée par le contrôle, et part en alerte S1 vers les bonnes personnes, sans donnée du dossier dans la
/// notification ; le centre d'alertes suit ensuite les droits de chacun.
/// </summary>
public sealed class IntegrityAlertApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private const string CustomerName = "Boulangerie Martin";

    private static Uri Api(string path) => new(path, UriKind.Relative);

    private static HttpClient InDossier(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }

    [Fact]
    public async Task Une_ligne_du_journal_modifiee_en_base_leve_une_S1_vers_les_bonnes_personnes()
    {
        RecordingChannel channel = new();
        using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<INotificationChannel>(channel);

                // Factures fictives, sans pièce en base : seule la chaîne est contrôlée ici.
                services.AddScoped<IJournaledDocumentReader, SyntheticDocuments>();
            }));
        using HttpClient manager = InDossier(host.CreateClient());

        // Trois factures validées comme le fera le module des pièces : numéro réservé, puis entrée au journal.
        TenantDescriptor dossier = (await host.Services.GetRequiredService<ITenantCatalog>().FindAsync(new DossierId(LettraApiFactory.DossierId), default))!;
        for (int i = 1; i <= 3; i++)
        {
            await DossierJobScope.RunAsync(host.Services.GetRequiredService<IServiceScopeFactory>(), dossier, async (sp, ct) =>
            {
                IIntegrityJournal journal = sp.GetRequiredService<IIntegrityJournal>();
                Result<string> validated = await sp.GetRequiredService<IDocumentNumbering>().ExecuteWithNextNumberAsync(
                    NumberSeriesCodes.Invoices,
                    async (number, token) => (await journal.AppendAsync(Invoice(number, 120m * i), token)).Map(_ => number),
                    ct);
                validated.IsSuccess.Should().BeTrue();
                return validated.Value;
            }, default);
        }

        IntegrityReportResponse intact = await VerifyAsync(manager);
        intact.Intact.Should().BeTrue();
        intact.EntriesChecked.Should().Be(3);
        channel.Sent.Should().BeEmpty();

        // Un administrateur de base retire la protection et retouche le montant de la deuxième facture.
        await using (SqliteConnection connection = new($"Data Source={factory.DossierDatabase};Pooling=False"))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                DROP TRIGGER "IntegrityJournal_NoUpdate";
                UPDATE "IntegrityJournal" SET "TotalInclTax" = '24.0000' WHERE "Sequence" = 2;
                """;
            await command.ExecuteNonQueryAsync();
        }

        IntegrityReportResponse tampered = await VerifyAsync(manager);
        tampered.Intact.Should().BeFalse();
        tampered.Findings.Should().ContainSingle().Which.Should().Be(new IntegrityFindingResponse("EntryAltered", 2, null, null));

        // Une notification S1, aux personnes qui ont AlertesLire sur ce dossier et aux administrateurs du cabinet.
        (AlertNotification notification, IReadOnlyCollection<Recipient> recipients) = channel.Sent.Should().ContainSingle().Subject;
        notification.Severity.Should().Be(Severity.S1);
        notification.ScopeLevel.Should().Be(ScopeLevel.Dossier);
        recipients.Select(r => Guid.Parse(r.Id)).Should().BeEquivalentTo(
            [LettraApiFactory.ManagerId, LettraApiFactory.CollaborateurId, LettraApiFactory.AdministrateurCabinetId]);

        // Rien du dossier dans la notification : ni numéro, ni montant, ni client, ni identifiant de dossier.
        string sent = $"{notification.Title}|{notification.Link}|{notification.AlertId}";
        sent.Should().NotContainAny("FAC-", "120", "240", "24.0000", CustomerName, LettraApiFactory.DossierId.ToString());

        // Le collaborateur la voit dans son dossier, pas au niveau du cabinet ; le manager la prend en charge.
        using HttpClient collaborator = InDossier(host.CreateClient());
        collaborator.DefaultRequestHeaders.Authorization = new("Bearer", LettraApiFactory.TokenFor(LettraApiFactory.CollaborateurId));
        AlertCentreResponse? dossierCentre = await collaborator.GetFromJsonAsync(Api("/api/v1/alerts"), ContractsJsonContext.Default.AlertCentreResponse);
        AlertSummary alert = dossierCentre!.Alerts.Should().ContainSingle().Subject;
        alert.RuleId.Should().Be(DossierAlertPublisher.ChainRuleId);
        alert.Severity.Should().Be(AlertSeverity.S1);
        (await collaborator.GetAsync(Api($"/api/v1/cabinets/{LettraApiFactory.CabinetId}/alerts"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using HttpClient cabinetManager = host.CreateClient();
        (await cabinetManager.PostAsync(Api($"/api/v1/cabinets/{LettraApiFactory.CabinetId}/alerts/{alert.Id}/acknowledge"), null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        AlertDetailResponse? detail = await cabinetManager.GetFromJsonAsync(
            Api($"/api/v1/cabinets/{LettraApiFactory.CabinetId}/alerts/{alert.Id}"), ContractsJsonContext.Default.AlertDetailResponse);
        detail!.Alert.State.Should().Be(Contracts.Alerts.AlertState.Acknowledged);

        // Un autre cabinet ne voit rien de celui-ci.
        using HttpClient other = host.CreateClient();
        other.DefaultRequestHeaders.Authorization = new("Bearer", LettraApiFactory.TokenFor(LettraApiFactory.AutreCabinetAdminId, cabinet: LettraApiFactory.OtherCabinetId));
        (await other.GetAsync(Api($"/api/v1/cabinets/{LettraApiFactory.CabinetId}/alerts"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Le_controle_et_les_archives_exigent_leurs_permissions()
    {
        using HttpClient collaborator = InDossier(factory.ClientFor(LettraApiFactory.CollaborateurId));

        (await collaborator.PostAsync(Api("/api/v1/integrity/verification"), null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await collaborator.PostAsync(Api("/api/v1/integrity/closings"), null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await collaborator.GetAsync(Api("/api/v1/integrity/closings/1/archive"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using HttpClient manager = InDossier(factory.CreateClient());
        (await manager.GetAsync(Api("/api/v1/integrity/closings"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await manager.GetAsync(Api("/api/v1/integrity/closings/1/archive"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<IntegrityReportResponse> VerifyAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsync(Api("/api/v1/integrity/verification"), null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.IntegrityReportResponse))!;
    }

    private static DocumentLifecycleEvent Invoice(string number, decimal ttc) => new(
        JournalEntryKind.InvoiceValidated,
        JournaledDocumentKind.Invoice,
        Guid.CreateVersion7(),
        NumberSeriesCodes.Invoices,
        number,
        new DateOnly(2026, 10, 1),
        Guid.CreateVersion7(),
        ttc / 1.2m,
        ttc - (ttc / 1.2m),
        ttc,
        "EUR",
        DocumentFingerprint.Compute("invoice-v1", number, CustomerName, IntegrityHash.Field(ttc)));

    private sealed class RecordingChannel : INotificationChannel
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(AlertNotification, IReadOnlyCollection<Recipient>)> _sent = new();

        public IReadOnlyList<(AlertNotification Notification, IReadOnlyCollection<Recipient> Recipients)> Sent => [.. _sent];

        public string Name => "test";

        public Task SendAsync(AlertNotification notification, IReadOnlyCollection<Recipient> recipients, CancellationToken cancellationToken)
        {
            _sent.Enqueue((notification, recipients));
            return Task.CompletedTask;
        }
    }

    private sealed class SyntheticDocuments : IJournaledDocumentReader
    {
        public bool IsConnected => false;

        public Task<IReadOnlyDictionary<Guid, string>> ComputeContentHashesAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }
}
