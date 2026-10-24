using System.Reflection;

namespace Lettra.Architecture.Tests;

/// <summary>Les assemblies de la solution, chargées par un type connu de chacune.</summary>
internal static class Assemblies
{
    public static readonly Assembly Calculations = typeof(Calculations.Rounding).Assembly;
    public static readonly Assembly Contracts = typeof(Contracts.Common.ErrorCodes).Assembly;
    public static readonly Assembly Speech = typeof(Speech.DictationFormatter).Assembly;
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
    public static readonly Assembly InfrastructureSpeech = typeof(Infrastructure.Speech.DependencyInjection).Assembly;
    public static readonly Assembly SpeechOnDevice = typeof(Infrastructure.Speech.OnDevice.DependencyInjection).Assembly;
    public static readonly Assembly SupervisionDomain = typeof(Supervision.Domain.SupervisionDomain).Assembly;
    public static readonly Assembly SupervisionApplication = typeof(Supervision.Application.DependencyInjection).Assembly;
    public static readonly Assembly SupervisionInfrastructure = typeof(Supervision.Infrastructure.DependencyInjection).Assembly;
    public static readonly Assembly SyncServer = typeof(Sync.Server.SyncServer).Assembly;
    public static readonly Assembly SyncClient = typeof(Sync.Client.SyncCoordinator).Assembly;
    public static readonly Assembly ExportsApplication = typeof(Exports.Application.DependencyInjection).Assembly;
    public static readonly Assembly ExportsInfrastructure = typeof(Exports.Infrastructure.DependencyInjection).Assembly;
    public static readonly Assembly Bootstrap = typeof(Bootstrap.LettraBootstrap).Assembly;
    public static readonly Assembly UiAbstractions = typeof(UI.Abstractions.ICustomersGateway).Assembly;
    public static readonly Assembly UiShared = typeof(UI.Shared.Routes).Assembly;
    public static readonly Assembly Printing = typeof(Printing.PdfDocumentRenderer).Assembly;
    public static readonly Assembly UiGatewayHttp = typeof(UI.Gateway.Http.DependencyInjection).Assembly;
    public static readonly Assembly UiGatewayInProcess = typeof(UI.Gateway.InProcess.DependencyInjection).Assembly;
    public static readonly Assembly AiSkills = typeof(Ai.Skills.SkillCatalog).Assembly;
    public static readonly Assembly AiPrivacy = typeof(Ai.Privacy.PseudonymizationSession).Assembly;
    public static readonly Assembly AiTools = typeof(Ai.Tools.DependencyInjection).Assembly;
    public static readonly Assembly AiOrchestration = typeof(Ai.Orchestration.AiOptions).Assembly;
    public static readonly Assembly AiClaude = typeof(Ai.Claude.ClaudeEngine).Assembly;
    public static readonly Assembly AiLocal = typeof(Ai.Local.LocalModelEngine).Assembly;

    public static readonly Assembly DemoData = typeof(DemoData.DemoDataOptions).Assembly;
    public static readonly Assembly Api = typeof(Api.Security.LettraAuthentication).Assembly;
    public static readonly Assembly Worker = typeof(Worker.WorkerIdentityOptions).Assembly;
    public static readonly Assembly Web = Assembly.Load("Lettra.Web");

    /// <summary>Les hôtes et le jeu de démonstration : hors de la matrice des couches, vérifiés par <c>HostRulesTests</c>.</summary>
    public static IEnumerable<Assembly> Hosts => [Api, Web, Worker, DemoData];

    public static IEnumerable<Assembly> All =>
    [
        Calculations, Contracts, Domain, Application, Persistence, PersistenceSqlite, PersistencePostgreSql,
        PersistenceSqlServer, Catalog, IdentityLocal, IdentityOidc, External, Observability, SupervisionDomain,
        SupervisionApplication, SupervisionInfrastructure, SyncServer, SyncClient, ExportsApplication, ExportsInfrastructure,
        Bootstrap, UiAbstractions, UiShared, Printing, UiGatewayHttp, UiGatewayInProcess,
        AiSkills, AiPrivacy, AiTools, AiOrchestration, AiClaude, AiLocal,
        Speech, InfrastructureSpeech, SpeechOnDevice,
    ];

    public static IEnumerable<string> LettraReferencesOf(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("Lettra.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
}
