using Lettra.Infrastructure.Catalog;
using Lettra.Infrastructure.Persistence.Hosting;
using Microsoft.Extensions.Options;

namespace Lettra.Worker;

public sealed class CabinetKeyRewrapJobOptions : IPeriodicJobOptions
{
    public const string SectionName = "Lettra:Worker:KeyRewrap";

    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromDays(1);

    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Passe quotidienne du KMS : le moteur Transit fait tourner seul la clé racine de chaque cabinet ; cette passe
/// réenveloppe la KEK de chaque cabinet par la dernière version, pour que les versions anciennes puissent être retirées.
/// Ni la KEK, ni les DEK, ni les données ne changent. Rien à faire quand les KEK sont déclarées dans la configuration.
/// </summary>
internal sealed partial class CabinetKeyRewrapJob(
    IServiceProvider services,
    IOptions<CabinetKeyRewrapJobOptions> options,
    TimeProvider clock,
    ILogger<CabinetKeyRewrapJob> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CabinetKeyRewrapJobOptions settings = options.Value;
        return settings.Enabled && services.GetService<ICabinetKeyVault>() is ICabinetKeyVault vault
            ? PeriodicPass.RunAsync(settings.InitialDelay, settings.Interval, clock, token => RunPassAsync(vault, token), stoppingToken)
            : Task.CompletedTask;
    }

    internal async Task RunPassAsync(ICabinetKeyVault vault, CancellationToken cancellationToken)
    {
        try
        {
            int rewrapped = await vault.RewrapAllAsync(cancellationToken).ConfigureAwait(false);
            LogPass(rewrapped);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Clés des cabinets : {Rewrapped} enveloppes renouvelées par le KMS")]
    private partial void LogPass(int rewrapped);

    [LoggerMessage(Level = LogLevel.Error, Message = "Passe de réenveloppement des clés des cabinets en échec")]
    private partial void LogFailed(Exception exception);
}
