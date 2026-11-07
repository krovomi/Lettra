using Lettra.Infrastructure.Catalog;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lettra.Persistence.Tests;

/// <summary>
/// La passe des demandes de bases telle que le Worker la fait tourner : une panne ne l'arrête pas, et elle n'existe que
/// sur une cellule qui lui laisse l'administration des bases.
/// </summary>
public sealed class DossierProvisioningJobTests
{
    [Fact]
    public async Task La_passe_mene_les_demandes()
    {
        Pass pass = new(done: 2);

        await Job(pass).RunPassAsync(default);

        pass.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Un_catalogue_injoignable_n_arrete_pas_le_Worker()
    {
        Pass pass = new(failure: new InvalidOperationException("catalogue hors ligne"));

        Func<Task> run = () => Job(pass).RunPassAsync(default);

        await run.Should().NotThrowAsync();
        pass.Calls.Should().Be(1);
    }

    [Fact]
    public async Task L_arret_du_Worker_n_est_pas_avale()
    {
        using CancellationTokenSource stopping = new();
        await stopping.CancelAsync();

        Func<Task> run = () => Job(new Pass(failure: new OperationCanceledException(stopping.Token))).RunPassAsync(stopping.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task La_passe_tourne_jusqu_a_l_arret_du_Worker()
    {
        Pass pass = new();
        using DossierProvisioningJob job = Job(pass, new DossierProvisioningJobOptions { Interval = TimeSpan.FromMilliseconds(20) });

        await job.StartAsync(default);
        while (pass.Calls < 2)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        await job.StopAsync(default);
        pass.Calls.Should().BeGreaterThanOrEqualTo(2);
    }

    [Theory]
    [InlineData("Worker", 1)]
    [InlineData("Host", 0)]
    [InlineData(null, 0)]
    public void Passe_enregistree_seulement_quand_le_Worker_administre_les_bases(string? mode, int expected)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Lettra:Catalog:DatabaseAdministration"] = mode })
            .Build();

        ServiceCollection services = new();
        services.AddDossierProvisioningJob(configuration);

        services.Count(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(DossierProvisioningJob))
            .Should().Be(expected);
    }

    private static DossierProvisioningJob Job(Pass pass, DossierProvisioningJobOptions? options = null) =>
        new(pass, Options.Create(options ?? new DossierProvisioningJobOptions()), TimeProvider.System, NullLogger<DossierProvisioningJob>.Instance);

    private sealed class Pass(int done = 0, Exception? failure = null) : IDossierRequestPass
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<int> RunRequestsAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return failure is null ? Task.FromResult(done) : Task.FromException<int>(failure);
        }
    }
}
