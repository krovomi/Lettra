using Lettra.Application.Abstractions.Outbox;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Diagnostics;
using Lettra.Infrastructure.Persistence.Hosting;
using Lettra.Infrastructure.Persistence.Integrity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Persistence.Outbox;

public sealed class OutboxDispatchJobOptions
{
    public const string SectionName = "Lettra:Outbox:Job";

    public bool Enabled { get; set; } = true;

    /// <summary>Une passe sur tous les dossiers actifs à cet intervalle.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Attente avant la première passe, le temps que les migrations de démarrage se terminent.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Passe de l'outbox (étape 5.4) : pour chaque dossier actif, envoie les messages dus. Un dossier en échec n'arrête
/// pas la passe ; aucune donnée de dossier n'est journalisée. Plusieurs instances peuvent tourner : le bail de chaque
/// message les départage.
/// </summary>
internal sealed partial class OutboxDispatchJob(
    IServiceProvider services,
    IServiceScopeFactory scopes,
    OutboxDossierSource dossiers,
    IOptions<OutboxDispatchJobOptions> options,
    TimeProvider clock,
    ILogger<OutboxDispatchJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        OutboxDispatchJobOptions settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        await PeriodicPass.RunAsync(settings.InitialDelay, settings.Interval, clock, RunPassAsync, stoppingToken).ConfigureAwait(false);
    }

    internal async Task<OutboxPass> RunPassAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<TenantDescriptor> active;
        try
        {
            active = await dossiers.List(services, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPassFailed(exception);
            return new OutboxPass(0, 0, 0);
        }

        int delivered = 0, retried = 0, parked = 0;
        long pending = 0, parkedTotal = 0;
        DateTimeOffset? oldest = null;
        foreach (TenantDescriptor dossier in active)
        {
            try
            {
                (OutboxPass pass, OutboxBacklog backlog) = await DossierJobScope.RunAsync(scopes, dossier, async (provider, token) =>
                {
                    IOutboxDispatcher dispatcher = provider.GetRequiredService<IOutboxDispatcher>();
                    OutboxPass done = await dispatcher.DispatchDueAsync(token).ConfigureAwait(false);
                    return (done, await dispatcher.GetBacklogAsync(token).ConfigureAwait(false));
                }, cancellationToken).ConfigureAwait(false);
                (delivered, retried, parked) = (delivered + pass.Delivered, retried + pass.Retried, parked + pass.Parked);
                (pending, parkedTotal) = (pending + backlog.Pending, parkedTotal + backlog.Parked);
                if (backlog.OldestPendingEnqueuedAt is { } enqueued && (oldest is null || enqueued < oldest))
                {
                    oldest = enqueued;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogDossierFailed(dossier.DossierId.Value, exception);
            }
        }

        if (delivered + retried + parked > 0)
        {
            LogPass(delivered, retried, parked);
        }

        // File de tous les dossiers actifs, pour les tableaux de bord : des nombres, aucun dossier nommé.
        LettraTelemetry.Outbox.Report(pending, parkedTotal, oldest is { } first ? (clock.GetUtcNow() - first).TotalSeconds : 0);

        return new OutboxPass(delivered, retried, parked);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox : {Delivered} envoyé(s), {Retried} à reprendre, {Parked} mis de côté")]
    private partial void LogPass(int delivered, int retried, int parked);

    [LoggerMessage(Level = LogLevel.Error, Message = "Passe de l'outbox impossible : catalogue illisible")]
    private partial void LogPassFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox du dossier {DossierId} en échec")]
    private partial void LogDossierFailed(Guid dossierId, Exception exception);
}

internal sealed record OutboxDossierSource(Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<TenantDescriptor>>> List);

public static class OutboxRegistration
{
    /// <summary>Outbox et répartiteur de toute base de dossier ; l'envoi immédiat suit <see cref="OutboxOptions"/>.</summary>
    internal static IServiceCollection AddOutboxStores(this IServiceCollection services)
    {
        services.AddOptions<OutboxOptions>();
        services.AddScoped<IOutbox, EfOutbox>();
        services.AddScoped<IOutboxDispatcher, EfOutboxDispatcher>();
        return services;
    }

    /// <summary>
    /// La passe de fond de l'outbox, pour le Worker (et l'API d'un mode sans Worker) : lit les réglages
    /// <c>Lettra:Outbox</c> et <c>Lettra:Outbox:Job</c>.
    /// </summary>
    public static IServiceCollection AddOutboxDispatch(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<TenantDescriptor>>> activeDossiers)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(activeDossiers);
        services.AddOptions<OutboxOptions>().Bind(configuration.GetSection(OutboxOptions.SectionName));
        services.AddOptions<OutboxDispatchJobOptions>().Bind(configuration.GetSection(OutboxDispatchJobOptions.SectionName));
        services.TryAddSingleton(new OutboxDossierSource(activeDossiers));
        services.AddSingleton<OutboxDispatchJob>();
        services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatchJob>());
        return services;
    }
}
