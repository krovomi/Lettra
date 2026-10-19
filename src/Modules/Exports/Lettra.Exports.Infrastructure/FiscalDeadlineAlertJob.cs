using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Tax;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Tax;
using Lettra.Infrastructure.Persistence.Hosting;
using Lettra.Infrastructure.Persistence.Integrity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lettra.Exports.Infrastructure;

/// <summary>Réglages de la passe d'alertes du calendrier fiscal (Worker), active par défaut.</summary>
public sealed class FiscalDeadlineAlertOptions
{
    public const string SectionName = "Lettra:Worker:FiscalCalendar";

    public bool Enabled { get; set; } = true;

    /// <summary>Une passe par jour suffit : les échéances sont des dates.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Attente avant la première passe, le temps que les migrations de démarrage se terminent.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Passe d'alertes du calendrier fiscal : pour chaque dossier actif, une échéance due dans les 7 jours et non marquée
/// déposée lève l'alerte S3 du dossier, une échéance suivie dépassée l'alerte S2 ; sans elles, les alertes se résolvent.
/// Le centre d'alertes route vers les rôles du dossier. Aucune donnée de dossier n'est journalisée ni envoyée.
/// </summary>
internal sealed partial class FiscalDeadlineAlertJob(
    IServiceScopeFactory scopes,
    IServiceProvider services,
    FiscalDossierSource dossierSource,
    TimeProvider clock,
    IOptions<FiscalDeadlineAlertOptions> options,
    ILogger<FiscalDeadlineAlertJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        FiscalDeadlineAlertOptions settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        await PeriodicPass.RunAsync(settings.InitialDelay, settings.Interval, clock, RunPassAsync, stoppingToken).ConfigureAwait(false);
    }

    internal async Task<(int Checked, int Soon, int Overdue, int Failed)> RunPassAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<TenantDescriptor> dossiers;
        try
        {
            dossiers = await dossierSource.ListAsync(services, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPassFailed(exception);
            return (0, 0, 0, 0);
        }

        DateOnly today = LegalCalendar.Today(clock);
        int checkedCount = 0, soon = 0, overdue = 0, failed = 0;
        foreach (TenantDescriptor dossier in dossiers)
        {
            try
            {
                (bool isSoon, bool isOverdue)? outcome = await DossierJobScope.RunAsync(scopes, dossier, async (provider, token) =>
                {
                    Result<IReadOnlyList<FiscalDeadlineResponse>> upcoming = await provider.GetRequiredService<TaxDeclarations>()
                        .UpcomingAsync(today, FiscalCalendar.DueSoonDays, token).ConfigureAwait(false);
                    if (upcoming.IsFailure)
                    {
                        // Fiche entreprise absente : rien à surveiller, et pas une panne.
                        LogSkipped(dossier.DossierId.Value, upcoming.Errors[0].Code);
                        return ((bool, bool)?)null;
                    }

                    bool dueSoon = upcoming.Value.Any(d => d.State == FiscalDeadlineStateDto.DueSoon);
                    bool late = upcoming.Value.Any(d => d.State == FiscalDeadlineStateDto.Overdue);
                    IDossierAlertPublisher alerts = provider.GetRequiredService<IDossierAlertPublisher>();
                    DateTimeOffset at = clock.GetUtcNow();
                    await alerts.PublishAsync(new DossierAlertSignal(DossierAlertRule.FiscalDeadlineSoon, dossier.OrganisationId, dossier.DossierId, dueSoon, at), token).ConfigureAwait(false);
                    await alerts.PublishAsync(new DossierAlertSignal(DossierAlertRule.FiscalDeadlineOverdue, dossier.OrganisationId, dossier.DossierId, late, at), token).ConfigureAwait(false);
                    return (dueSoon, late);
                }, cancellationToken).ConfigureAwait(false);

                checkedCount++;
                soon += outcome is { isSoon: true } ? 1 : 0;
                overdue += outcome is { isOverdue: true } ? 1 : 0;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                LogDossierFailed(dossier.DossierId.Value, exception);
            }
        }

        LogPass(checkedCount, soon, overdue, failed);
        return (checkedCount, soon, overdue, failed);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Calendrier fiscal : {Checked} dossiers, {Soon} avec une échéance proche, {Overdue} en retard, {Failed} en échec")]
    private partial void LogPass(int @checked, int soon, int overdue, int failed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Passe du calendrier fiscal impossible : catalogue illisible")]
    private partial void LogPassFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Calendrier fiscal du dossier {DossierId} sans objet : {Code}")]
    private partial void LogSkipped(Guid dossierId, string code);

    [LoggerMessage(Level = LogLevel.Error, Message = "Calendrier fiscal du dossier {DossierId} en échec")]
    private partial void LogDossierFailed(Guid dossierId, Exception exception);
}

/// <summary>Les dossiers actifs de la cellule pour la passe fiscale, fournis par l'hôte.</summary>
internal sealed class FiscalDossierSource(Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<TenantDescriptor>>> list)
{
    public Task<IReadOnlyList<TenantDescriptor>> ListAsync(IServiceProvider services, CancellationToken cancellationToken) => list(services, cancellationToken);
}
