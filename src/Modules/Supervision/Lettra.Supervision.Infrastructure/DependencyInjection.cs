using Lettra.Supervision.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lettra.Supervision.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Module Supervision : cas d'usage, ingestion, adapters du squelette et webhook Alertmanager.</summary>
    public static IServiceCollection AddSupervision(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<SupervisionOptions>(configuration.GetSection(SupervisionOptions.SectionName));
        services.AddSingleton<IAlertStore, InMemoryAlertStore>();
        services.AddSingleton<IOnCallSchedule, ConfiguredOnCallSchedule>();
        services.AddSingleton<IRoleDirectory, PendingRoleDirectory>();
        services.AddSingleton<INotificationChannel, LogNotificationChannel>();
        services.AddSingleton(sp => new SupervisionLinks(sp.GetRequiredService<IOptions<SupervisionOptions>>().Value.PublicBaseAddress));
        return services.AddSupervisionApplication();
    }
}
