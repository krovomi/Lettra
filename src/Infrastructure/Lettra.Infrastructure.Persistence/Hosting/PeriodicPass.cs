namespace Lettra.Infrastructure.Persistence.Hosting;

/// <summary>
/// La boucle des tâches de fond périodiques (intégrité, échéances fiscales, vue cabinet, outbox) : une attente le temps
/// que les migrations de démarrage se terminent, une première passe, puis une passe par intervalle jusqu'à l'arrêt. Les
/// deux attentes passent par l'horloge injectée, pas par l'horloge système.
/// </summary>
public static class PeriodicPass
{
    public static async Task RunAsync(
        TimeSpan initialDelay,
        TimeSpan interval,
        TimeProvider clock,
        Func<CancellationToken, Task> pass,
        CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(pass);
        await Task.Delay(initialDelay, clock, stoppingToken).ConfigureAwait(false);
        using PeriodicTimer timer = new(interval, clock);
        do
        {
            await pass(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
