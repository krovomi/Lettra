using System.Reflection;

namespace Lettra.Architecture.Tests;

/// <summary>Les assemblies de la solution, chargées par un type connu de chacune.</summary>
internal static class Assemblies
{
    public static readonly Assembly Calculations = typeof(Calculations.Rounding).Assembly;
    public static readonly Assembly Contracts = typeof(Contracts.Common.ErrorCodes).Assembly;
    public static readonly Assembly Domain = typeof(Domain.Parties.Customer).Assembly;
    public static readonly Assembly Application = typeof(Application.DependencyInjection).Assembly;
    public static readonly Assembly Persistence = typeof(Infrastructure.Persistence.LettraDbContext).Assembly;
    public static readonly Assembly PersistenceSqlite = typeof(Infrastructure.Persistence.Sqlite.SqliteConfigurator).Assembly;
    public static readonly Assembly PersistencePostgreSql = typeof(Infrastructure.Persistence.PostgreSql.PostgreSqlConfigurator).Assembly;
    public static readonly Assembly PersistenceSqlServer = typeof(Infrastructure.Persistence.SqlServer.SqlServerConfigurator).Assembly;
    public static readonly Assembly Catalog = typeof(Infrastructure.Catalog.CatalogOptions).Assembly;
    public static readonly Assembly IdentityLocal = typeof(Infrastructure.Identity.Local.LocalIdentityOptions).Assembly;
    public static readonly Assembly IdentityOidc = typeof(Infrastructure.Identity.Oidc.DependencyInjection).Assembly;
    public static readonly Assembly External = typeof(Infrastructure.External.DependencyInjection).Assembly;
    public static readonly Assembly Observability = typeof(Infrastructure.Observability.DependencyInjection).Assembly;
    public static readonly Assembly SupervisionDomain = typeof(Supervision.Domain.SupervisionDomain).Assembly;
    public static readonly Assembly SupervisionApplication = typeof(Supervision.Application.DependencyInjection).Assembly;
    public static readonly Assembly SupervisionInfrastructure = typeof(Supervision.Infrastructure.DependencyInjection).Assembly;
    public static readonly Assembly Bootstrap = typeof(Bootstrap.LettraBootstrap).Assembly;
    public static readonly Assembly UiAbstractions = typeof(UI.Abstractions.ICustomersGateway).Assembly;
    public static readonly Assembly UiShared = typeof(UI.Shared.Routes).Assembly;
    public static readonly Assembly UiGatewayHttp = typeof(UI.Gateway.Http.DependencyInjection).Assembly;
    public static readonly Assembly UiGatewayInProcess = typeof(UI.Gateway.InProcess.DependencyInjection).Assembly;

    public static IEnumerable<Assembly> All =>
    [
        Calculations, Contracts, Domain, Application, Persistence, PersistenceSqlite, PersistencePostgreSql,
        PersistenceSqlServer, Catalog, IdentityLocal, IdentityOidc, External, Observability, SupervisionDomain,
        SupervisionApplication, SupervisionInfrastructure, Bootstrap, UiAbstractions, UiShared, UiGatewayHttp, UiGatewayInProcess,
    ];

    public static IEnumerable<string> LettraReferencesOf(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("Lettra.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
}
