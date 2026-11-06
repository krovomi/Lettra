using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lettra.Worker.Tests;

public sealed class CabinetKeyRewrapJobTests
{
    [Fact]
    public async Task La_passe_reenveloppe_les_cles_de_tous_les_cabinets()
    {
        Vault vault = new(rewrapped: 3);

        await Job(new ServiceCollection().BuildServiceProvider()).RunPassAsync(vault, default);

        vault.Passes.Should().Be(1);
    }

    [Fact]
    public async Task Un_KMS_en_panne_n_arrete_pas_le_Worker()
    {
        Vault vault = new(failure: new HttpRequestException("KMS injoignable"));

        Func<Task> pass = () => Job(new ServiceCollection().BuildServiceProvider()).RunPassAsync(vault, default);

        await pass.Should().NotThrowAsync();
        vault.Passes.Should().Be(1);
    }

    [Fact]
    public async Task Une_annulation_n_est_pas_avalee()
    {
        Vault vault = new(failure: new OperationCanceledException());

        Func<Task> pass = () => Job(new ServiceCollection().BuildServiceProvider()).RunPassAsync(vault, default);

        await pass.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Sans_KMS_la_passe_ne_demarre_pas()
    {
        using CabinetKeyRewrapJob job = Job(new ServiceCollection().BuildServiceProvider());

        await job.StartAsync(default);

        Task execution = job.ExecuteTask!;
        (await Task.WhenAny(execution, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(execution, "sans KMS, la passe s'arrête aussitôt");
        execution.IsCompletedSuccessfully.Should().BeTrue();
        await job.StopAsync(default);
    }

    private static CabinetKeyRewrapJob Job(IServiceProvider services) => new(
        services,
        Options.Create(new CabinetKeyRewrapJobOptions()),
        TimeProvider.System,
        NullLogger<CabinetKeyRewrapJob>.Instance);

    private sealed class Vault(int rewrapped = 0, Exception? failure = null) : ICabinetKeyVault
    {
        public int Passes { get; private set; }

        public Task<int> RewrapAllAsync(CancellationToken cancellationToken)
        {
            Passes++;
            return failure is null ? Task.FromResult(rewrapped) : Task.FromException<int>(failure);
        }

        public Task<bool> RewrapAsync(OrganisationId organisation, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task RotateAsync(OrganisationId organisation, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DestroyAsync(OrganisationId organisation, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
