using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Cabinet;
using Lettra.Supervision.Domain;

namespace Lettra.Supervision.Application;

/// <summary>
/// Alertes ouvertes du dossier (nouvelles ou prises en charge) pour la vue cabinet. Lues dans la base de supervision,
/// sans ouvrir la base du dossier ; une alerte S1 ou S2 rend l'indicateur critique.
/// </summary>
internal sealed class OpenAlertsIndicatorSource(IAlertStore alerts, ITenantContext tenant) : IDossierIndicatorSource
{
    private const int Limit = 200;

    public async Task<IReadOnlyList<DossierIndicator>> ComputeAsync(DateOnly today, CancellationToken cancellationToken)
    {
        IReadOnlyList<Alert> within = await alerts
            .ListWithinAsync(AuthorizationScope.ForDossier(tenant.Current.DossierId), includeClosed: false, Limit, cancellationToken)
            .ConfigureAwait(false);
        Alert[] open = [.. within.Where(a => a.State is AlertState.New or AlertState.Acknowledged)];
        IndicatorLevel level = open.Length == 0 ? IndicatorLevel.Ok
            : open.Any(a => a.Severity <= Severity.S2) ? IndicatorLevel.Critical
            : IndicatorLevel.Warning;
        return [new DossierIndicator(DossierIndicatorKeys.OpenAlerts, open.Length, level)];
    }
}
