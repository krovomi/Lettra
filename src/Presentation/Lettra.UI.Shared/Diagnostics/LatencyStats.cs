using System.Globalization;

namespace Lettra.UI.Shared.Diagnostics;

/// <summary>Résumé d'une série de latences (millisecondes) : médiane, p95 au rang le plus proche, maximum.</summary>
public sealed record LatencyStats(int Count, double Median, double P95, double Max)
{
    public static LatencyStats From(IReadOnlyCollection<double> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            return new LatencyStats(0, 0, 0, 0);
        }

        double[] sorted = [.. samples.Order()];
        return new LatencyStats(sorted.Length, Percentile(sorted, 0.50), Percentile(sorted, 0.95), sorted[^1]);
    }

    /// <summary>Rang le plus proche : la plus petite valeur dont au moins <paramref name="p"/> des mesures sont en dessous ou égales.</summary>
    public static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sorted.Count);
        int rank = (int)Math.Ceiling(p * sorted.Count);
        return sorted[Math.Clamp(rank, 1, sorted.Count) - 1];
    }

    public static string Ms(double value) => Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + " ms";
}

/// <summary>Résultat brut renvoyé par <c>grid-benchmark.js</c>.</summary>
public sealed record GridBenchmarkRun(double[] Navigation, double[] Entry, double[] Amount, int Missed, int Rows);

/// <summary>Environnement de la mesure, pour la relire plus tard.</summary>
public sealed record GridBenchmarkEnvironment(string UserAgent, int Cores, int Width, int Height, double PixelRatio);
