using System.Globalization;
using System.Text;
using Lettra.Application.Abstractions.Kpi;
using Lettra.Application.Features.Kpi;
using Lettra.Domain.SharedKernel;

namespace Lettra.Infrastructure.Observability;

/// <summary>
/// Écrit une collecte d'indicateurs au format texte de Prometheus (version 0.0.4), lu par le Prometheus de la cellule.
/// Ce chemin ne passe pas par le collecteur OpenTelemetry : il ne sort jamais vers un fournisseur hébergé. Seuls les
/// indicateurs du catalogue (<see cref="KpiMetrics.Help"/>) sont écrits ; une valeur non finie est omise.
/// </summary>
public static class KpiExposition
{
    public const string ContentType = "text/plain; version=0.0.4; charset=utf-8";

    public static string Write(KpiSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Dictionary<DossierId, KpiDossier> dossiers = snapshot.Dossiers.ToDictionary(d => d.Id);
        ILookup<string, KpiSample> byMetric = snapshot.Samples
            .Where(s => double.IsFinite(s.Value))
            .Where(s => s.Dossier is null || dossiers.ContainsKey(s.Dossier.Value))
            .ToLookup(s => s.Metric, StringComparer.Ordinal);

        StringBuilder text = new();
        foreach ((string metric, string help) in KpiMetrics.Help)
        {
            if (!byMetric.Contains(metric))
            {
                continue;
            }

            text.Append("# HELP ").Append(metric).Append(' ').Append(EscapeHelp(help)).Append('\n');
            text.Append("# TYPE ").Append(metric).Append(" gauge\n");
            foreach (KpiSample sample in byMetric[metric])
            {
                text.Append(metric);
                AppendLabels(text, sample, sample.Dossier is DossierId id ? dossiers[id] : null);
                text.Append(' ').Append(sample.Value.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            }
        }

        return text.ToString();
    }

    private static void AppendLabels(StringBuilder text, KpiSample sample, KpiDossier? dossier)
    {
        List<(string Name, string Value)> labels = [];
        if (dossier is not null)
        {
            labels.Add(("dossier_id", dossier.Id.Value.ToString()));
            labels.Add(("dossier", dossier.Name));
            if (sample.Metric == KpiMetrics.DossierInfo)
            {
                labels.Add(("cabinet", dossier.Cabinet));
            }
        }

        if (sample.Label is { } own)
        {
            labels.Add((own.Name, own.Value));
        }

        if (labels.Count == 0)
        {
            return;
        }

        text.Append('{');
        for (int i = 0; i < labels.Count; i++)
        {
            text.Append(i == 0 ? string.Empty : ",").Append(labels[i].Name).Append("=\"").Append(EscapeLabel(labels[i].Value)).Append('"');
        }

        text.Append('}');
    }

    private static string EscapeLabel(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    private static string EscapeHelp(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
}
