using Lettra.Application.Abstractions.Kpi;
using Lettra.Application.Features.Kpi;
using Lettra.Domain.SharedKernel;
using Lettra.Supervision.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Supervision.Application;

/// <summary>
/// Alertes ouvertes de la cellule (nouvelles ou prises en charge) par gravité, pour le tableau d'administration. Lues
/// dans la base de supervision ; ni titre, ni dossier, ni destinataire : un nombre par gravité.
/// </summary>
internal sealed class OpenAlertsKpiSource(IServiceScopeFactory scopes) : ICellKpiSource
{
    public async Task<IReadOnlyList<KpiSample>> CollectAsync(IReadOnlyCollection<DossierId> dossiers, DateOnly today, CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            IReadOnlyList<Alert> active = await scope.ServiceProvider.GetRequiredService<IAlertStore>()
                .ListActiveAsync(cancellationToken).ConfigureAwait(false);
            return Count(active);
        }
    }

    internal static IReadOnlyList<KpiSample> Count(IEnumerable<Alert> alerts)
    {
        Alert[] open = [.. alerts.Where(a => a.State is AlertState.New or AlertState.Acknowledged)];
        return
        [
            .. Enum.GetValues<Severity>().Select(severity => new KpiSample(
                KpiMetrics.OpenAlerts,
                open.Count(a => a.Severity == severity),
                Label: new KpiLabel(KpiMetrics.Labels.Severity, severity.ToString()))),
        ];
    }
}
