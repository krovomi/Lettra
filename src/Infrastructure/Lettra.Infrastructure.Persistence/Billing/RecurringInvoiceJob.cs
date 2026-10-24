using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Contracts.Billing;
using Lettra.Domain.Common;
using Lettra.Infrastructure.Persistence.Hosting;
using Lettra.Infrastructure.Persistence.Integrity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Persistence.Billing;

/// <summary>Réglages de la passe des factures récurrentes (Worker, ou API sans Worker), active par défaut.</summary>
public sealed class RecurringInvoiceJobOptions : IPeriodicJobOptions
{
    public const string SectionName = "Lettra:Worker:RecurringInvoices";

    public bool Enabled { get; set; } = true;

    /// <summary>Les échéances sont des dates : quelques passes par jour suffisent.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Attente avant la première passe, le temps que les migrations de démarrage se terminent.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Compte de service propre à la passe, pour un hôte sans identité de service (l'API d'une installation sans
    /// Worker, comme la démonstration). Déclaré au registre des droits comme tout utilisateur : ses rôles disent ce
    /// qu'il peut faire (<c>FactureSaisir</c>, et <c>FactureValider</c> pour la validation automatique). Vide, la passe
    /// agit sous l'identité de l'hôte (le compte de service du Worker).
    /// </summary>
    public Guid? ServiceAccountId { get; set; }
}

/// <summary>Le compte sous lequel agit le travail de fond de la portée courante ; vide hors d'une passe.</summary>
internal sealed class RecurringJobActor
{
    public Guid UserId { get; set; }
}

/// <summary>
/// Identité vue par les cas d'usage : le compte de service de la passe dans la portée d'une passe, l'identité de
/// l'hôte (requête HTTP, Worker) partout ailleurs. Les droits, la séparation des tâches et la traçabilité s'appliquent
/// donc à ce compte comme à un utilisateur.
/// </summary>
internal sealed class RecurringJobCurrentUser(RecurringJobActor actor, ICurrentUser host) : ICurrentUser
{
    public bool IsAuthenticated => actor.UserId != Guid.Empty || host.IsAuthenticated;

    public Guid UserId => actor.UserId != Guid.Empty ? actor.UserId : host.UserId;
}

/// <summary>
/// Passe des factures récurrentes : pour chaque dossier actif, <see cref="GenerateRecurringInvoicesCommand"/> sous le
/// compte de service de la passe (<see cref="RecurringInvoiceJobOptions.ServiceAccountId"/>) ou, à défaut, sous
/// l'identité de l'hôte (le compte de service du Worker), avec ses droits et la séparation des tâches du cabinet.
/// Sans aucune identité, la passe ne fait rien : les factures se créent alors depuis l'écran. Un dossier en échec n'arrête
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

        RecurringInvoiceJobOptions settings = options.Value;
        int created = 0, validated = 0, failed = 0;
        foreach (TenantDescriptor dossier in active)
        {
            try
            {
                Result<RecurringRunResponse>? run = await DossierJobScope.RunAsync(scopes, dossier, async (provider, token) =>
                {
                    if (settings.ServiceAccountId is Guid account && provider.GetService<RecurringJobActor>() is { } actor)
                    {
                        actor.UserId = account;
                    }

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
    /// La passe de fond des factures récurrentes : lit <c>Lettra:Worker:RecurringInvoices</c> ; l'hôte fournit la liste
    /// des dossiers actifs. Les factures sont créées sous <c>ServiceAccountId</c> s'il est renseigné (l'identité de
    /// l'hôte, <c>ICurrentUser</c>, doit alors être enregistrée avant cet appel), sinon sous l'identité de l'hôte.
    /// </summary>
    public static IServiceCollection AddRecurringInvoicing(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<TenantDescriptor>>> activeDossiers)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(activeDossiers);
        IConfigurationSection section = configuration.GetSection(RecurringInvoiceJobOptions.SectionName);
        services.AddOptions<RecurringInvoiceJobOptions>().Bind(section).ValidatePeriodicJob(RecurringInvoiceJobOptions.SectionName);
        if (section.Get<RecurringInvoiceJobOptions>()?.ServiceAccountId is not null)
        {
            UseServiceAccount(services);
        }

        services.AddSingleton(new RecurringDossierSource(activeDossiers));
        services.AddSingleton<RecurringInvoiceJob>();
        services.AddHostedService(sp => sp.GetRequiredService<RecurringInvoiceJob>());
        return services;
    }

    /// <summary>Enveloppe l'identité de l'hôte : le compte de service ne vaut que dans la portée d'une passe.</summary>
    private static void UseServiceAccount(IServiceCollection services)
    {
        ServiceDescriptor host = services.LastOrDefault(d => d.ServiceType == typeof(ICurrentUser) && !d.IsKeyedService)
            ?? throw new InvalidOperationException("L'identité de l'hôte (ICurrentUser) doit être enregistrée avant AddRecurringInvoicing.");
        services.Remove(host);
        services.AddScoped<RecurringJobActor>();
        services.AddScoped<ICurrentUser>(sp => new RecurringJobCurrentUser(sp.GetRequiredService<RecurringJobActor>(), Resolve(sp, host)));
    }

    private static ICurrentUser Resolve(IServiceProvider services, ServiceDescriptor host) =>
        (ICurrentUser)(host.ImplementationInstance
            ?? host.ImplementationFactory?.Invoke(services)
            ?? ActivatorUtilities.CreateInstance(services, host.ImplementationType!));
}
