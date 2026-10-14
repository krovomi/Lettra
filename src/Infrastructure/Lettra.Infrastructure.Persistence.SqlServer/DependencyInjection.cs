using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Infrastructure.Persistence.SqlServer;

public static class DependencyInjection
{
    public static IServiceCollection AddSqlServerProvider(this IServiceCollection services) =>
        services
            .AddSingleton<IDbProviderConfigurator, SqlServerConfigurator>()
            .AddSingleton<IDatabaseAdministrator, SqlServerDatabaseAdministrator>();
}
