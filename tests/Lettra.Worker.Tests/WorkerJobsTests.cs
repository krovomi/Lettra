using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Lettra.Worker.Tests;

public sealed class WorkerJobsTests
{
    [Fact]
    public void En_job_de_deploiement_le_worker_ne_fait_que_migrer()
    {
        ServiceDescriptor job = HostedServices(runOnce: true).Should().ContainSingle().Subject;

        job.ImplementationType.Should().Be<DossierMigrationJob>();
    }

    [Fact]
    public void En_continu_le_worker_fait_la_migration_et_les_huit_passes()
    {
        List<ServiceDescriptor> jobs = HostedServices(runOnce: false);

        jobs.Should().HaveCount(9);
        jobs[0].ImplementationType.Should().Be<DossierMigrationJob>();
    }

    [Fact]
    public void Le_reglage_RunOnce_est_lu_sous_Lettra_Worker()
    {
        ServiceCollection services = new();
        services.AddWorkerJobs(Configuration(runOnce: true));
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<DossierMigrationJobOptions>>().Value.RunOnce.Should().BeTrue();
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", false)]
    [InlineData("0199e3a0-0000-7000-8000-0000000005e1", true)]
    public void Le_compte_de_service_n_est_authentifie_que_s_il_est_configure(string userId, bool authenticated)
    {
        WorkerCurrentUser user = new(Options.Create(new WorkerIdentityOptions { UserId = Guid.Parse(userId) }));

        user.IsAuthenticated.Should().Be(authenticated);
        user.UserId.Should().Be(Guid.Parse(userId));
    }

    private static List<ServiceDescriptor> HostedServices(bool runOnce)
    {
        ServiceCollection services = new();
        services.AddWorkerJobs(Configuration(runOnce));
        return [.. services.Where(d => d.ServiceType == typeof(IHostedService))];
    }

    private static IConfiguration Configuration(bool runOnce) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Lettra:Worker:RunOnce"] = runOnce ? "true" : "false" })
        .Build();
}
