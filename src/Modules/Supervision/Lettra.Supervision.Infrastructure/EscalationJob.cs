using Lettra.Supervision.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lettra.Supervision.Infrastructure;

public sealed class EscalationJobOptions
{
    public const string SectionName = "Lettra:Worker:Escalation";

    public bool Enabled { get; set; } = true;

    /// <summary>Les délais d'escalade se comptent en minutes : une passe par minute suffit.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// Passe d'escalade du module Supervision (annexe G) : alertes non prises en charge escaladées, alertes résolues
/// et calmes clôturées, par le Worker ou, dans une installation sans Worker (démonstration), par l'API. Une passe qui
/// échoue est journalisée ; la suivante repart de l'état du registre.
/// </summary>
internal sealed partial class EscalationJob(
    IServiceScopeFactory scopes,
    IOptions<EscalationJobOptions> options,
    TimeProvider clock,
    ILogger<EscalationJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EscalationJobOptions settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        using PeriodicTimer timer = new(settings.Interval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                AsyncServiceScope scope = scopes.CreateAsyncScope();
                await using (scope.ConfigureAwait(false))
                {
                    (int escalated, int closed) = await scope.ServiceProvider.GetRequiredService<EscalationRun>()
                        .RunAsync(stoppingToken).ConfigureAwait(false);
                    if (escalated + closed > 0)
                    {
                        LogPass(escalated, closed);
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Escalade : {Escalated} alertes escaladées, {Closed} clôturées")]
    private partial void LogPass(int escalated, int closed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Passe d'escalade en échec")]
    private partial void LogFailed(Exception exception);
}

public static class EscalationHosting
{
    /// <summary>Passe d'escalade du centre d'alertes ; réglages sous <c>Lettra:Worker:Escalation</c>, quel que soit l'hôte.</summary>
    public static IServiceCollection AddEscalationJob(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<EscalationJobOptions>().Bind(configuration.GetSection(EscalationJobOptions.SectionName))
            .Validate(o => o.Interval > TimeSpan.Zero, $"{EscalationJobOptions.SectionName}:{nameof(EscalationJobOptions.Interval)} doit être strictement positif.")
            .ValidateOnStart();
        services.AddHostedService<EscalationJob>();
        return services;
    }
}
