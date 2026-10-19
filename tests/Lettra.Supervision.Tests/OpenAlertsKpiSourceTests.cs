using Lettra.Application.Abstractions.Kpi;
using Lettra.Application.Features.Kpi;
using Lettra.Supervision.Application;
using Lettra.Supervision.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Supervision.Tests;

/// <summary>Alertes ouvertes par gravité pour Grafana : un nombre par gravité, rien d'autre de l'alerte.</summary>
public sealed class OpenAlertsKpiSourceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);

    private static Alert Raise(string rule, Severity severity) =>
        Alert.Raise(rule, "Titre", severity, AlertCategory.Availability, AlertScope.ForCell("demo").Value, T0).Value;

    [Fact]
    public async Task Les_alertes_nouvelles_et_prises_en_charge_sont_comptees_par_gravite()
    {
        Alert acknowledged = Raise("b", Severity.S2);
        acknowledged.Acknowledge(Actor.Human(Guid.CreateVersion7()), T0).IsSuccess.Should().BeTrue();
        Alert resolved = Raise("c", Severity.S1);
        resolved.Acknowledge(Actor.Human(Guid.CreateVersion7()), T0);
        resolved.Resolve(Actor.Human(Guid.CreateVersion7()), T0).IsSuccess.Should().BeTrue();

        ServiceCollection services = new();
        services.AddScoped<IAlertStore>(_ => new FixedAlerts([Raise("a", Severity.S1), acknowledged, resolved, Raise("d", Severity.S4)]));
        await using ServiceProvider provider = services.BuildServiceProvider();
        OpenAlertsKpiSource source = new(provider.GetRequiredService<IServiceScopeFactory>());

        IReadOnlyList<KpiSample> samples = await source.CollectAsync([], DateOnly.FromDateTime(T0.Date), default);

        samples.Should().HaveCount(4).And.OnlyContain(s => s.Metric == KpiMetrics.OpenAlerts && s.Dossier == null);
        samples.ToDictionary(s => s.Label!.Value, s => s.Value).Should().BeEquivalentTo(
            new Dictionary<string, double> { ["S1"] = 1, ["S2"] = 1, ["S3"] = 0, ["S4"] = 1 });
    }

    private sealed class FixedAlerts(IReadOnlyList<Alert> alerts) : IAlertStore
    {
        public Task<IReadOnlyList<Alert>> ListActiveAsync(CancellationToken cancellationToken) => Task.FromResult(alerts);

        public Task<Alert?> FindByFingerprintAsync(string fingerprint, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Alert?> GetAsync(AlertId id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Alert>> ListWithinAsync(Lettra.Application.Abstractions.Identity.AuthorizationScope scope, bool includeClosed, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveAsync(Alert alert, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
