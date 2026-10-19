using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Contracts.Billing;
using Lettra.Domain.Common;
using Lettra.Infrastructure.Persistence.Integrity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Persistence.Billing;

/// <summary>Réglages de la passe des factures récurrentes (Worker), active par défaut.</summary>
public sealed class RecurringInvoiceJobOptions
{
    public const string SectionName = "Lettra:Worker:RecurringInvoices";

    public bool Enabled { get; set; } = true;

    /// <summary>Les échéances sont des dates : quelques passes par jour suffisent.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Attente avant la première passe, le temps que les migrations de démarrage se terminent.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(3);
}

/// <summary>
/// Passe des factures récurrentes : pour chaque dossier actif, <see cref="GenerateRecurringInvoicesCommand"/> sous
/// l'identité de l'hôte (le compte de service du Worker, avec ses droits et la séparation des tâches du cabinet).
/// Sans identité, la passe ne fait rien : les factures se créent alors depuis l'écran. Un dossier en échec n'arrête
/// pas la passe ; aucune donnée de dossier n'est journalisée.
/// </summary>
internal sealed partial class RecurringInvoiceJob(
    IServiceProvider services,
    IServiceScopeFactory scopes,
    RecurringDossierSource dossiers,
    IOptions<RecurringInvoiceJobOptions> options,
    ILogger<RecurringInvoiceJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RecurringInvoiceJobOptions settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        await Task.Delay(settings.InitialDelay, stoppingToken).ConfigureAwait(false);
        using PeriodicTimer timer = new(settings.Interval);
        do
        {
            await RunPassAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    internal async Task<(int Created, int Validated, int Failed)> RunPassAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<TenantDescriptor> active;
        try
        {
            active = await dossiers.List(services, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPassFailed(exception);
            return (0, 0, 0);
        }

        int created = 0, validated = 0, failed = 0;
        foreach (TenantDescriptor dossier in active)
        {
            try
            {
                Result<RecurringRunResponse>? run = await DossierJobScope.RunAsync(scopes, dossier, async (provider, token) =>
                {
                    if (!provider.GetRequiredService<ICurrentUser>().IsAuthenticated)
                    {
                        return (Result<RecurringRunResponse>?)null;
                    }

                    return await provider.GetRequiredService<ICommandDispatcher>()
                        .SendAsync(new GenerateRecurringInvoicesCommand(dossier.DossierId), token).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);

                if (run is null)
                {
                    LogNoIdentity();
                    return (created, validated, failed);
                }

                if (run.Value.IsFailure)
                {
                    failed++;
                    LogDossierRefused(dossier.DossierId.Value, run.Value.Errors[0].Code);
                    continue;
                }

                created += run.Value.Value.CreatedCount;
                validated += run.Value.Value.ValidatedCount;
                failed += run.Value.Value.Failures.Count;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                LogDossierFailed(dossier.DossierId.Value, exception);
            }
        }

        if (created + failed > 0)
        {
            LogPass(created, validated, failed);
        }

        return (created, validated, failed);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Factures récurrentes : {Created} créée(s), dont {Validated} validée(s), {Failed} en échec")]
    private partial void LogPass(int created, int validated, int failed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Factures récurrentes : aucune identité de service, la passe est laissée à l'écran")]
    private partial void LogNoIdentity();

    [LoggerMessage(Level = LogLevel.Error, Message = "Passe des factures récurrentes impossible : catalogue illisible")]
    private partial void LogPassFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Factures récurrentes du dossier {DossierId} refusées : {Code}")]
    private partial void LogDossierRefused(Guid dossierId, string code);

    [LoggerMessage(Level = LogLevel.Error, Message = "Factures récurrentes du dossier {DossierId} en échec")]
    private partial void LogDossierFailed(Guid dossierId, Exception exception);
}

internal sealed record RecurringDossierSource(Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<TenantDescriptor>>> List);

public static class RecurringInvoiceRegistration
{
    /// <summary>
    /// La passe de fond des factures récurrentes, pour le Worker : lit <c>Lettra:Worker:RecurringInvoices</c> ; l'hôte
    /// fournit la liste des dossiers actifs et l'identité (<c>ICurrentUser</c>) sous laquelle les factures sont créées.
    /// </summary>
    public static IServiceCollection AddRecurringInvoicing(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<TenantDescriptor>>> activeDossiers)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(activeDossiers);
        services.AddOptions<RecurringInvoiceJobOptions>().Bind(configuration.GetSection(RecurringInvoiceJobOptions.SectionName));
        services.AddSingleton(new RecurringDossierSource(activeDossiers));
        services.AddSingleton<RecurringInvoiceJob>();
        services.AddHostedService(sp => sp.GetRequiredService<RecurringInvoiceJob>());
        return services;
    }
}
