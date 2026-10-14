using Lettra.Supervision.Domain;

namespace Lettra.Supervision.Application;

/// <summary>
/// Passe périodique du Worker : escalade des alertes que personne n'a prises en charge, et clôture des alertes
/// résolues restées calmes sept jours. Déterministe ; Vigie n'y participe pas.
/// </summary>
public sealed class EscalationRun(IAlertStore store, EscalationPolicy policy, AlertIngestion ingestion, TimeProvider clock)
{
    public async Task<(int Escalated, int Closed)> RunAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        int escalated = 0;
        int closed = 0;
        foreach (Alert alert in await store.ListActiveAsync(cancellationToken).ConfigureAwait(false))
        {
            if (policy.Apply(alert, now) is not null)
            {
                escalated++;
                await store.SaveAsync(alert, cancellationToken).ConfigureAwait(false);
                await ingestion.NotifyAsync(alert, cancellationToken).ConfigureAwait(false);
            }
            else if (alert.CloseIfQuiet(now))
            {
                closed++;
                await store.SaveAsync(alert, cancellationToken).ConfigureAwait(false);
            }
        }

        return (escalated, closed);
    }
}
