using Lettra.Infrastructure.Persistence.Supervision;
using Lettra.Supervision.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        services.AddScoped<IRoleDirectory, PermissionRoleDirectory>();
        services.AddSingleton<INotificationChannel, LogNotificationChannel>();
        services.AddSingleton(sp => new SupervisionLinks(sp.GetRequiredService<IOptions<SupervisionOptions>>().Value.PublicBaseAddress));
        return services.AddSupervisionApplication();
    }

    /// <summary>
    /// Base de supervision (annexe G §9) à la place du stockage en mémoire. Le provider et la chaîne de connexion
    /// viennent de la composition root ; par défaut la base Catalog, sous le schéma <c>supervision</c>.
    /// </summary>
    public static IServiceCollection AddSupervisionDatabase(this IServiceCollection services, Action<DbContextOptionsBuilder> configure)
    {
        services.AddPooledDbContextFactory<SupervisionDbContext>(configure);
        services.Replace(ServiceDescriptor.Scoped<IAlertStore, EfAlertStore>());
        services.AddSingleton<SupervisionDatabaseInitializer>();
        return services;
    }

    /// <summary>Migre la base de supervision si elle est déclarée ; sans elle, rien à faire.</summary>
    public static Task MigrateSupervisionAsync(this IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetService<SupervisionDatabaseInitializer>()?.MigrateAsync(cancellationToken) ?? Task.CompletedTask;
}
