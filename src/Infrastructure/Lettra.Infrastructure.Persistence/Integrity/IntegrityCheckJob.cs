using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Integrity;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Infrastructure.Persistence.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Persistence.Integrity;

public sealed class IntegrityCheckJobOptions
{
    public const string SectionName = "Lettra:Worker:Integrity";

    public bool Enabled { get; set; } = true;

    /// <summary>Une passe par jour : clôture des mois écoulés puis contrôle de chaque chaîne.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Attente avant la première passe, le temps que les migrations de démarrage se terminent.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Âge au-delà duquel les clés de la chaîne d'un dossier tournent (annexe F : chaque année). Zéro : jamais.</summary>
    public TimeSpan KeyRotationAge { get; set; } = TimeSpan.FromDays(365);
}

/// <summary>
/// Passe quotidienne de la chaîne anti-fraude (5 min après le démarrage, puis toutes les 24 h), par le Worker ou, dans une
/// installation sans Worker (démonstration), par l'API : pour chaque dossier actif, clôture et signe les mois écoulés, fait tourner
/// les clés devenues trop anciennes, puis contrôle la chaîne. Un constat lève l'alerte de dossier (S1 chaîne compromise, S2 trou de numérotation), un contrôle
/// propre la résout. Un dossier en échec n'arrête pas la passe ; aucune donnée de dossier n'est journalisée.
/// </summary>
internal sealed partial class IntegrityCheckJob(
    IServiceProvider services,
    Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<TenantDescriptor>>> listDossiers,
    IServiceScopeFactory scopes,
    IOptions<IntegrityCheckJobOptions> options,
    TimeProvider clock,
    ILogger<IntegrityCheckJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IntegrityCheckJobOptions settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        await PeriodicPass.RunAsync(settings.InitialDelay, settings.Interval, clock, RunPassAsync, stoppingToken).ConfigureAwait(false);
    }

    internal async Task<(int Checked, int Compromised, int Failed)> RunPassAsync(CancellationToken cancellationToken)
    {
        IntegrityCheckJobOptions settings = options.Value;
        int checkedCount = 0;
        int compromised = 0;
        int failed = 0;
        IReadOnlyList<TenantDescriptor> dossiers;
        try
        {
            dossiers = await listDossiers(services, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPassFailed(exception);
            return (0, 0, 0);
        }

        foreach (TenantDescriptor dossier in dossiers)
        {
            try
            {
                Result<IntegrityReport> report = await DossierJobScope.RunAsync(scopes, dossier, async (provider, token) =>
                {
                    DossierIntegrityService integrity = provider.GetRequiredService<DossierIntegrityService>();
                    Result<IReadOnlyList<PeriodClosing>> closed = await integrity.CloseDuePeriodsAsync(token).ConfigureAwait(false);
                    if (closed.IsFailure)
                    {
                        LogClosingFailed(dossier.DossierId.Value, closed.Errors[0].Code);
                    }

                    if (settings.KeyRotationAge > TimeSpan.Zero)
                    {
                        Result<IntegrityKeyVersion?> rotated = await integrity.RotateKeysIfDueAsync(settings.KeyRotationAge, token).ConfigureAwait(false);
                        if (rotated.IsFailure)
                        {
                            LogRotationFailed(dossier.DossierId.Value, rotated.Errors[0].Code);
                        }
                    }

                    return await integrity.VerifyAsync(token).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);

                checkedCount++;
                if (report.IsFailure)
                {
                    failed++;
                    LogCheckFailed(dossier.DossierId.Value, report.Errors[0].Code);
                }
                else if (!report.Value.IsIntact)
                {
                    compromised++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                LogDossierFailed(dossier.DossierId.Value, exception);
            }
        }

        LogPass(checkedCount, compromised, failed);
        return (checkedCount, compromised, failed);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Intégrité : {Checked} dossiers contrôlés, {Compromised} avec constat, {Failed} en échec")]
    private partial void LogPass(int @checked, int compromised, int failed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Passe d'intégrité impossible : catalogue illisible")]
    private partial void LogPassFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Clôture du dossier {DossierId} en échec : {Code}")]
    private partial void LogClosingFailed(Guid dossierId, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rotation des clés du dossier {DossierId} en échec : {Code}")]
    private partial void LogRotationFailed(Guid dossierId, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Contrôle d'intégrité du dossier {DossierId} impossible : {Code}")]
    private partial void LogCheckFailed(Guid dossierId, string code);

    [LoggerMessage(Level = LogLevel.Error, Message = "Contrôle d'intégrité du dossier {DossierId} en échec")]
    private partial void LogDossierFailed(Guid dossierId, Exception exception);
}

public static class IntegrityCheckHosting
{
    /// <summary>
    /// Passe quotidienne de la chaîne anti-fraude. <paramref name="listDossiers"/> donne les dossiers actifs de la cellule
    /// (le catalogue) ; réglages sous <c>Lettra:Worker:Integrity</c>, quel que soit l'hôte.
    /// </summary>
    public static IServiceCollection AddIntegrityCheckJob(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<TenantDescriptor>>> listDossiers)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<IntegrityCheckJobOptions>().Bind(configuration.GetSection(IntegrityCheckJobOptions.SectionName));
        services.AddHostedService(sp => new IntegrityCheckJob(
            sp, listDossiers, sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<IOptions<IntegrityCheckJobOptions>>(),
            sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<IntegrityCheckJob>>()));
        return services;
    }
}
