namespace Lettra.Infrastructure.Observability;

/// <summary>
/// Bornes des histogrammes de durée HTTP. Les objectifs de service de l'annexe E (lecture p95 &lt; 300 ms,
/// validation p95 &lt; 800 ms) exigent une borne exacte à 0,3 s et à 0,8 s : sans elles, Prometheus
/// interpolerait entre 0,25 et 0,5 s et le taux de requêtes « assez rapides » serait faux.
/// </summary>
public static class SloBuckets
{
    public const double ReadLatencyObjectiveSeconds = 0.3;

    public const double ValidationLatencyObjectiveSeconds = 0.8;

    public static IReadOnlyList<double> HttpServerDuration { get; } =
        [0.005, 0.01, 0.025, 0.05, 0.1, 0.2, ReadLatencyObjectiveSeconds, 0.5, ValidationLatencyObjectiveSeconds, 1, 2.5, 5, 10];
}
