using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Infrastructure.Persistence.PostgreSql;

public static class DependencyInjection
{
    public static IServiceCollection AddPostgreSqlProvider(this IServiceCollection services) =>
        services
            .AddSingleton<IDbProviderConfigurator, PostgreSqlConfigurator>()
            .AddSingleton<IDatabaseAdministrator, PostgreSqlDatabaseAdministrator>();
}
