using Lettra.Infrastructure.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lettra.Persistence.Tests;

/// <summary>La passe d'effacement des dossiers telle que le Worker la fait tourner : une panne ne l'arrête pas.</summary>
public sealed class DossierErasureJobTests
{
    [Fact]
    public async Task La_passe_efface_les_dossiers_dus()
    {
        Pass pass = new(erased: 2);

        await Job(pass).RunPassAsync(default);

        pass.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Un_serveur_de_bases_injoignable_n_arrete_pas_le_Worker()
    {
        Pass pass = new(failure: new InvalidOperationException("catalogue hors ligne"));

        Func<Task> run = () => Job(pass).RunPassAsync(default);

        await run.Should().NotThrowAsync();
        pass.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Une_annulation_n_est_pas_avalee()
    {
        Func<Task> run = () => Job(new Pass(failure: new OperationCanceledException())).RunPassAsync(default);

        await run.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Passe_desactivee_n_efface_rien()
    {
        Pass pass = new();
        using DossierErasureJob job = Job(pass, new DossierErasureJobOptions { Enabled = false, InitialDelay = TimeSpan.Zero });

        await job.StartAsync(default);

        Task execution = job.ExecuteTask!;
        (await Task.WhenAny(execution, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(execution, "désactivée, la passe s'arrête aussitôt");
        pass.Calls.Should().Be(0);
        await job.StopAsync(default);
    }

    private static DossierErasureJob Job(Pass pass, DossierErasureJobOptions? options = null) =>
        new(pass, Options.Create(options ?? new DossierErasureJobOptions()), TimeProvider.System, NullLogger<DossierErasureJob>.Instance);

    private sealed class Pass(int erased = 0, Exception? failure = null) : IDossierErasurePass
    {
        public int Calls { get; private set; }

        public Task<int> EraseDueAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return failure is null ? Task.FromResult(erased) : Task.FromException<int>(failure);
        }
    }
}
