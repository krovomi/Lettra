using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Integrity;
using Lettra.Infrastructure.Persistence.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Passe quotidienne de la chaîne anti-fraude sur SQLite, telle que le Worker ou l'API d'une installation sans Worker la
/// font tourner. Données fictives.
/// </summary>
public sealed class IntegrityCheckJobTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lettra-integrite-").FullName;

    private string Database(string name) => $"Data Source={Path.Combine(_folder, name)}.db;Pooling=False";

    [Fact]
    public async Task Passe_controle_chaque_dossier_et_compte_les_constats()
    {
        await using TenantHarness harness = new(new SqliteConfigurator(), syntheticDocuments: true);
        await IntegrityScenarios.TamperDetectedAsync(harness, DatabaseProvider.Sqlite, Database("alteree"));
        DossierId intact = harness.AddDossier(DatabaseProvider.Sqlite, Database("intacte"));
        await using (AsyncServiceScope scope = await harness.OpenAsync(intact))
        {
            await IntegrityScenarios.ValidateAsync(scope.ServiceProvider, 120m);
        }

        TenantDescriptor[] dossiers = [.. harness.Alerts.Signals.Select(s => s.Dossier).Append(intact).Distinct().Select(harness.Descriptor)];

        (int Checked, int Compromised, int Failed) outcome = await Job(harness, (_, _) => Task.FromResult<IReadOnlyList<TenantDescriptor>>(dossiers)).RunPassAsync(default);

        outcome.Should().Be((2, 1, 0));
    }

    [Fact]
    public async Task Passe_avec_un_catalogue_illisible_ne_controle_rien()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());

        (int Checked, int Compromised, int Failed) outcome = await Job(harness, (_, _) => throw new InvalidOperationException("catalogue hors ligne")).RunPassAsync(default);

        outcome.Should().Be((0, 0, 0));
    }

    [Fact]
    public async Task Passe_desactivee_ne_lit_pas_le_catalogue()
    {
        await using TenantHarness harness = new(new SqliteConfigurator());
        int calls = 0;
        using IntegrityCheckJob job = Job(harness, (_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<IReadOnlyList<TenantDescriptor>>([]);
        }, new IntegrityCheckJobOptions { Enabled = false, InitialDelay = TimeSpan.Zero });

        await job.StartAsync(default);
        await job.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        await job.StopAsync(default);

        calls.Should().Be(0);
    }

    [Fact]
    public void Enregistrement_de_la_passe_lit_ses_reglages_sous_Lettra_Worker_Integrity()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lettra:Worker:Integrity:Enabled"] = "false",
            ["Lettra:Worker:Integrity:Interval"] = "06:00:00",
            ["Lettra:Worker:Integrity:InitialDelay"] = "00:00:30",
        }).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddIntegrityCheckJob(configuration, (_, _) => Task.FromResult<IReadOnlyList<TenantDescriptor>>([]));
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetServices<IHostedService>().Should().ContainSingle().Which.Should().BeOfType<IntegrityCheckJob>();
        IntegrityCheckJobOptions options = provider.GetRequiredService<IOptions<IntegrityCheckJobOptions>>().Value;
        options.Enabled.Should().BeFalse();
        options.Interval.Should().Be(TimeSpan.FromHours(6));
        options.InitialDelay.Should().Be(TimeSpan.FromSeconds(30));

        Action withoutConfiguration = () => new ServiceCollection().AddIntegrityCheckJob(null!, (_, _) => Task.FromResult<IReadOnlyList<TenantDescriptor>>([]));
        withoutConfiguration.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Reglages_par_defaut_de_la_passe()
    {
        IntegrityCheckJobOptions options = new();
        options.Enabled.Should().BeTrue();
        options.Interval.Should().Be(TimeSpan.FromDays(1));
        options.InitialDelay.Should().Be(TimeSpan.FromMinutes(5));
        options.KeyRotationAge.Should().Be(TimeSpan.FromDays(365));
        IntegrityCheckJobOptions.SectionName.Should().Be("Lettra:Worker:Integrity");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    private static IntegrityCheckJob Job(
        TenantHarness harness,
        Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<TenantDescriptor>>> listDossiers,
        IntegrityCheckJobOptions? options = null) =>
        new(harness.Services, listDossiers, harness.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options ?? new IntegrityCheckJobOptions()), TimeProvider.System, NullLogger<IntegrityCheckJob>.Instance);
}
