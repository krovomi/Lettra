using System.Collections.Concurrent;
using Lettra.Supervision.Application;
using Lettra.Supervision.Domain;
using Lettra.Supervision.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Lettra.Supervision.Tests;

/// <summary>Passe d'escalade, telle que le Worker ou l'API d'une installation sans Worker la font tourner.</summary>
public sealed class EscalationJobTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task La_passe_escalade_une_S1_non_prise_en_charge()
    {
        FakeTimeProvider clock = new(T0);
        RecordingChannel channel = new();
        await using ServiceProvider services = Services(clock, channel, new Dictionary<string, string?> { ["Lettra:Worker:Escalation:Interval"] = "00:00:00.050" });
        await services.GetRequiredService<AlertIngestion>().IngestAsync(
            new AlertSignal("LettraApiIndisponible", "API Lettra indisponible", Severity.S1, AlertCategory.Availability, AlertScope.ForCell("mut-fr-01").Value, true, T0),
            CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(16));
        IHostedService job = services.GetServices<IHostedService>().Should().ContainSingle().Subject;

        await job.StartAsync(CancellationToken.None);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (channel.Sent.Count < 2 && !timeout.IsCancellationRequested)
        {
            await Task.Delay(20, CancellationToken.None);
        }

        await job.StopAsync(CancellationToken.None);

        channel.Sent.Should().HaveCount(2);
        channel.Sent.Last().Should().ContainSingle().Which.Id.Should().Be("astreinte-n2");
    }

    [Fact]
    public async Task La_passe_desactivee_ne_fait_rien()
    {
        await using ServiceProvider services = Services(new FakeTimeProvider(T0), new RecordingChannel(), new Dictionary<string, string?>
        {
            ["Lettra:Worker:Escalation:Enabled"] = "false",
            ["Lettra:Worker:Escalation:Interval"] = "00:00:00.050",
        });
        BackgroundService job = services.GetServices<IHostedService>().Should().ContainSingle().Which.Should().BeAssignableTo<BackgroundService>().Subject;

        await job.StartAsync(CancellationToken.None);
        await job.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        await job.StopAsync(CancellationToken.None);

        job.ExecuteTask.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void Enregistrement_de_la_passe_lit_ses_reglages_sous_Lettra_Worker_Escalation()
    {
        using ServiceProvider services = Services(new FakeTimeProvider(T0), new RecordingChannel(), new Dictionary<string, string?> { ["Lettra:Worker:Escalation:Interval"] = "00:05:00" });

        services.GetServices<IHostedService>().Should().ContainSingle().Which.Should().BeOfType<EscalationJob>();
        services.GetRequiredService<IOptions<EscalationJobOptions>>().Value.Interval.Should().Be(TimeSpan.FromMinutes(5));
        new EscalationJobOptions().Should().BeEquivalentTo(new { Enabled = true, Interval = TimeSpan.FromMinutes(1) });
        EscalationJobOptions.SectionName.Should().Be("Lettra:Worker:Escalation");

        Action withoutConfiguration = () => new ServiceCollection().AddEscalationJob(null!);
        withoutConfiguration.Should().Throw<ArgumentNullException>();
    }

    private static ServiceProvider Services(FakeTimeProvider clock, RecordingChannel channel, Dictionary<string, string?> settings)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSupervision(configuration);
        services.AddFakeDirectory(new FakeDirectory());
        services.AddSingleton<INotificationChannel>(channel);
        services.AddEscalationJob(configuration);
        return services.BuildServiceProvider();
    }

    private sealed class RecordingChannel : INotificationChannel
    {
        /// <summary>Destinataires de chaque envoi, dans l'ordre (la passe envoie depuis son propre fil).</summary>
        public ConcurrentQueue<IReadOnlyCollection<Recipient>> Sent { get; } = new();

        public string Name => "test";

        public Task SendAsync(AlertNotification notification, IReadOnlyCollection<Recipient> recipients, CancellationToken cancellationToken)
        {
            Sent.Enqueue(recipients);
            return Task.CompletedTask;
        }
    }
}
