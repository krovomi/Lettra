using System.Text.Json;
using Lettra.Domain.Common;
using Lettra.Supervision.Application;
using Lettra.Supervision.Domain;
using Lettra.Supervision.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Supervision.Tests;

/// <summary>De l'Alertmanager au destinataire : dédoublonnage, routage, escalade, sans IA sur le chemin.</summary>
public sealed class IngestionTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock = new(T0);
    private readonly RecordingChannel _channel = new();
    private readonly ServiceProvider _services;

    public IngestionTests()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSupervision(configuration);
        services.AddSingleton<INotificationChannel>(_channel);
        _services = services.BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    private static AlertSignal ApiDown(bool firing, DateTimeOffset at) =>
        new("LettraApiIndisponible", "API Lettra indisponible", Severity.S1, AlertCategory.Availability, AlertScope.ForCell("mut-fr-01").Value, firing, at);

    [Fact]
    public async Task Le_meme_signal_repete_ne_cree_qu_une_alerte_et_une_notification()
    {
        AlertIngestion ingestion = _services.GetRequiredService<AlertIngestion>();

        Result<AlertId> first = await ingestion.IngestAsync(ApiDown(true, T0), CancellationToken.None);
        Result<AlertId> second = await ingestion.IngestAsync(ApiDown(true, T0.AddMinutes(1)), CancellationToken.None);

        second.Value.Should().Be(first.Value);
        _channel.Sent.Should().ContainSingle();
        _channel.Sent[0].Recipients.Should().ContainSingle().Which.Should().Be(new Recipient(RecipientKind.OnCall, "astreinte-n1"));
        (await _services.GetRequiredService<IAlertStore>().GetAsync(first.Value, CancellationToken.None))!.Occurrences.Should().Be(2);
    }

    [Fact]
    public async Task La_notification_ne_porte_que_titre_gravite_portee_et_lien()
    {
        await _services.GetRequiredService<AlertIngestion>().IngestAsync(ApiDown(true, T0), CancellationToken.None);

        typeof(AlertNotification).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo("AlertId", "Title", "Severity", "ScopeLevel", "Link", "EscalationLevel");
        _channel.Sent[0].Notification.Link.ToString().Should().StartWith("https://localhost/alertes/");
    }

    [Fact]
    public async Task Une_S1_non_prise_en_charge_est_escaladee_au_second_niveau_d_astreinte()
    {
        await _services.GetRequiredService<AlertIngestion>().IngestAsync(ApiDown(true, T0), CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(16));

        using IServiceScope scope = _services.CreateScope();
        (int escalated, _) = await scope.ServiceProvider.GetRequiredService<EscalationRun>().RunAsync(CancellationToken.None);

        escalated.Should().Be(1);
        _channel.Sent.Should().HaveCount(2);
        _channel.Sent[1].Recipients.Should().ContainSingle().Which.Id.Should().Be("astreinte-n2");
        _channel.Sent[1].Notification.EscalationLevel.Should().Be(1);
    }

    [Fact]
    public async Task Le_signal_resolu_resout_l_alerte_par_sa_regle()
    {
        AlertIngestion ingestion = _services.GetRequiredService<AlertIngestion>();
        AlertId id = (await ingestion.IngestAsync(ApiDown(true, T0), CancellationToken.None)).Value;

        await ingestion.IngestAsync(ApiDown(false, T0.AddMinutes(3)), CancellationToken.None);

        Alert alert = (await _services.GetRequiredService<IAlertStore>().GetAsync(id, CancellationToken.None))!;
        alert.State.Should().Be(AlertState.Resolved);
        alert.Timeline[^1].Actor.Kind.Should().Be(ActorKind.Rule);
    }

    [Fact]
    public void Une_alerte_Alertmanager_bien_etiquetee_devient_un_signal()
    {
        AlertmanagerPayload payload = JsonSerializer.Deserialize(Sample, AlertmanagerJsonContext.Default.AlertmanagerPayload)!;

        Result<AlertSignal> signal = AlertmanagerMapper.ToSignal(payload.Alerts![0]);

        signal.IsSuccess.Should().BeTrue();
        signal.Value.Should().BeEquivalentTo(new
        {
            RuleId = "LettraApiIndisponible",
            Title = "API Lettra indisponible",
            Severity = Severity.S1,
            Category = AlertCategory.Availability,
            Firing = true,
        });
        signal.Value.Scope.Should().Be(AlertScope.ForCell("mut-fr-01").Value);
    }

    [Fact]
    public void Une_alerte_sans_gravite_n_est_pas_devinee()
    {
        AlertmanagerAlert alert = new("firing", new Dictionary<string, string> { ["alertname"] = "X", ["scope"] = "cell", ["cell"] = "c" }, null, T0, default);

        AlertmanagerMapper.ToSignal(alert).Errors.Should().ContainSingle(e => e.Code == AlertmanagerMapper.UnmappedCode);
    }

    [Theory]
    [InlineData("Bearer s3cr3t", true)]
    [InlineData("Bearer s3cr3", false)]
    [InlineData("s3cr3t", false)]
    [InlineData("", false)]
    public void Le_webhook_exige_le_jeton_de_la_cellule(string header, bool expected) =>
        SupervisionEndpoints.IsAuthorized(header, "s3cr3t").Should().Be(expected);

    private const string Sample = """
        {
          "version": "4",
          "status": "firing",
          "receiver": "lettra-supervision",
          "alerts": [{
            "status": "firing",
            "labels": { "alertname": "LettraApiIndisponible", "severity": "S1", "category": "availability", "scope": "cell", "cell": "mut-fr-01" },
            "annotations": { "title": "API Lettra indisponible", "runbook_url": "https://runbooks/api" },
            "startsAt": "2026-10-14T08:00:00Z",
            "endsAt": "0001-01-01T00:00:00Z"
          }]
        }
        """;

    private sealed class RecordingChannel : INotificationChannel
    {
        public List<(AlertNotification Notification, IReadOnlyCollection<Recipient> Recipients)> Sent { get; } = [];

        public string Name => "test";

        public Task SendAsync(AlertNotification notification, IReadOnlyCollection<Recipient> recipients, CancellationToken cancellationToken)
        {
            Sent.Add((notification, recipients));
            return Task.CompletedTask;
        }
    }
}
