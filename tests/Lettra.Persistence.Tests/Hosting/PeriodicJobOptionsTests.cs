using Lettra.Infrastructure.Persistence.Cabinet;
using Lettra.Infrastructure.Persistence.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lettra.Persistence.Tests.Hosting;

/// <summary>Le calendrier d'une tâche de fond est vérifié au démarrage de l'hôte, pas à sa première passe.</summary>
public sealed class PeriodicJobOptionsTests
{
    [Theory]
    [InlineData("00:00:00", "00:00:05", "Lettra:CabinetOverview:Interval doit être strictement positif.")]
    [InlineData("-01:00:00", "00:00:05", "Lettra:CabinetOverview:Interval doit être strictement positif.")]
    [InlineData("06:00:00", "-00:00:01", "Lettra:CabinetOverview:InitialDelay ne peut pas être négatif.")]
    public void Un_calendrier_invalide_empeche_le_demarrage_en_nommant_le_reglage(string interval, string initialDelay, string message)
    {
        using ServiceProvider services = Services(interval, initialDelay);

        Action start = () => services.GetRequiredService<IStartupValidator>().Validate();

        start.Should().Throw<OptionsValidationException>().Which.Failures.Should().Contain(message);
    }

    [Fact]
    public void Un_calendrier_valide_demarre()
    {
        using ServiceProvider services = Services("06:00:00", "00:00:00");

        services.GetRequiredService<IStartupValidator>().Validate();

        services.GetRequiredService<IOptions<CabinetOverviewJobOptions>>().Value.Interval.Should().Be(TimeSpan.FromHours(6));
    }

    private static ServiceProvider Services(string interval, string initialDelay)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{CabinetOverviewJobOptions.SectionName}:Interval"] = interval,
            [$"{CabinetOverviewJobOptions.SectionName}:InitialDelay"] = initialDelay,
        }).Build();
        ServiceCollection services = [];
        services.AddOptions<CabinetOverviewJobOptions>()
            .Bind(configuration.GetSection(CabinetOverviewJobOptions.SectionName))
            .ValidatePeriodicJob(CabinetOverviewJobOptions.SectionName);
        return services.BuildServiceProvider();
    }
}
