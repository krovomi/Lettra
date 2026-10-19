using Lettra.Infrastructure.Persistence.Hosting;

namespace Lettra.Persistence.Tests.Hosting;

/// <summary>La boucle commune des tâches de fond : une passe tout de suite après l'attente, puis une par intervalle.</summary>
public sealed class PeriodicPassTests
{
    [Fact]
    public async Task Les_passes_se_suivent_jusqu_a_l_arret()
    {
        using CancellationTokenSource stop = new();
        int passes = 0;

        Func<Task> run = () => PeriodicPass.RunAsync(TimeSpan.Zero, TimeSpan.FromMilliseconds(5), TimeProvider.System, _ =>
        {
            if (Interlocked.Increment(ref passes) == 3)
            {
                stop.Cancel();
            }

            return Task.CompletedTask;
        }, stop.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
        passes.Should().Be(3);
    }

    [Fact]
    public async Task Un_arret_pendant_l_attente_initiale_n_execute_aucune_passe()
    {
        using CancellationTokenSource stop = new();
        int passes = 0;
        Task running = PeriodicPass.RunAsync(TimeSpan.FromHours(1), TimeSpan.FromHours(1), TimeProvider.System, _ =>
        {
            passes++;
            return Task.CompletedTask;
        }, stop.Token);

        await stop.CancelAsync();

        await FluentActions.Awaiting(() => running).Should().ThrowAsync<OperationCanceledException>();
        passes.Should().Be(0);
    }

    [Fact]
    public async Task La_passe_recoit_le_jeton_d_arret()
    {
        using CancellationTokenSource stop = new();
        CancellationToken received = default;

        Func<Task> run = () => PeriodicPass.RunAsync(TimeSpan.Zero, TimeSpan.FromHours(1), TimeProvider.System, token =>
        {
            received = token;
            stop.Cancel();
            return Task.CompletedTask;
        }, stop.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
        received.Should().Be(stop.Token);
    }
}
