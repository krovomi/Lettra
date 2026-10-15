using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using Lettra.Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Lettra.Api.Tests;

/// <summary>
/// L'API en mode 2 sur un dossier SQLite temporaire, avec capture de tous les journaux (niveau Trace).
/// Les jetons sont signés par une clé de test à la place de Keycloak ; le client par défaut agit comme
/// <see cref="ManagerId"/>, manager du cabinet du dossier, MFA prouvée.
/// </summary>
public sealed class LettraApiFactory : WebApplicationFactory<Program>
{
    public static readonly Guid DossierId = new("0199e3a0-0000-7000-8000-00000000a001");
    public static readonly Guid CabinetId = new("0199e3a0-0000-7000-8000-0000000000c1");
    public static readonly Guid OtherCabinetId = new("0199e3a0-0000-7000-8000-0000000000c2");
    public static readonly Guid ManagerId = new("0199e3a0-0000-7000-8000-00000000e001");
    public static readonly Guid CollaborateurId = new("0199e3a0-0000-7000-8000-00000000e002");
    public static readonly Guid PortailId = new("0199e3a0-0000-7000-8000-00000000e003");
    public static readonly Guid AutreCabinetAdminId = new("0199e3a0-0000-7000-8000-00000000e004");
    public static readonly Guid SuspenduId = new("0199e3a0-0000-7000-8000-00000000e005");
    public static readonly Guid AdministrateurCabinetId = new("0199e3a0-0000-7000-8000-00000000e006");

    private const string Issuer = "https://keycloak.test/realms/lettra";
    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("cle-de-test-lettra-uniquement-pour-les-tests-0123456789"));

    private readonly string _folder = Directory.CreateTempSubdirectory("lettra-api-").FullName;

    public ConcurrentQueue<string> Logs { get; } = new();

    public static Dictionary<string, string?> DossierSettings(string database) => new()
    {
        ["ConnectionStrings:Catalog"] = $"Data Source={database}.catalog;Pooling=False",
        ["Lettra:Dossiers:0:Id"] = DossierId.ToString(),
        ["Lettra:Dossiers:0:OrganisationId"] = CabinetId.ToString(),
        ["Lettra:Dossiers:0:Provider"] = "Sqlite",
        ["Lettra:Dossiers:0:ConnectionSecretRef"] = "config:Test",
        ["ConnectionStrings:Test"] = $"Data Source={database};Pooling=False",
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration(config =>
        {
            Dictionary<string, string?> settings = DossierSettings(Path.Combine(_folder, "api.db"));
            settings["Lettra:DeploymentMode"] = "LocalNetwork";
            settings["Lettra:Identity:Oidc:Authority"] = Issuer;

            // Création de dossiers : la clé du cabinet de démonstration et un serveur SQLite local.
            settings["Lettra:Organisations:0:Kek"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            settings["Lettra:Clusters:local:Provider"] = "Sqlite";
            settings["Lettra:Clusters:local:Directory"] = Path.Combine(_folder, "dossiers");
            foreach ((string key, string? value) in AuthorizationSettings())
            {
                settings[key] = value;
            }

            config.AddInMemoryCollection(settings);
        });
        builder.ConfigureTestServices(services =>
            services.PostConfigure<JwtBearerOptions>(LettraAuthSchemes.Bearer, o =>
            {
                o.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                o.TokenValidationParameters.IssuerSigningKey = SigningKey;
                o.TokenValidationParameters.ValidIssuer = Issuer;
            }));
        builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(new CapturingLoggerProvider(Logs));
        });
    }

    /// <summary>Utilisateurs du test : manager, administrateur et collaborateur du cabinet, client du portail, administrateur d'un autre cabinet, compte suspendu.</summary>
    private static Dictionary<string, string?> AuthorizationSettings()
    {
        Dictionary<string, string?> s = new();
        void User(int i, Guid id, Guid cabinet, string role, string scope, Guid scopeId, string state = "Active")
        {
            string p = $"Lettra:Authorization:Users:{i}:";
            s[p + "Id"] = id.ToString();
            s[p + "Cabinet"] = cabinet.ToString();
            s[p + "State"] = state;
            s[p + "Assignments:0:Role"] = role;
            s[p + "Assignments:0:Scope"] = scope;
            s[p + "Assignments:0:ScopeId"] = scopeId.ToString();
        }

        User(0, ManagerId, CabinetId, "manager", "Cabinet", CabinetId);
        User(1, CollaborateurId, CabinetId, "collaborateur", "Dossier", DossierId);
        User(2, PortailId, CabinetId, "client-portail", "Dossier", DossierId);
        User(3, AutreCabinetAdminId, OtherCabinetId, "manager", "Cabinet", OtherCabinetId);
        User(4, SuspenduId, CabinetId, "manager", "Cabinet", CabinetId, state: "Suspended");
        User(5, AdministrateurCabinetId, CabinetId, "administrateur-cabinet", "Cabinet", CabinetId);
        return s;
    }

    /// <summary>Jeton d'accès de test, comme Keycloak l'émettrait (sub, amr, organization, audience).</summary>
    public static string TokenFor(Guid userId, bool mfa = true, Guid? cabinet = null, string audience = "lettra-api")
    {
        Dictionary<string, object> claims = new()
        {
            ["sub"] = userId.ToString(),
            ["preferred_username"] = "test",
            ["amr"] = mfa ? new[] { "pwd", "otp" } : new[] { "pwd" },
            ["organization"] = new Dictionary<string, object> { ["cabinet"] = new Dictionary<string, object> { ["id"] = (cabinet ?? CabinetId).ToString() } },
        };
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Claims = claims,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>Client authentifié comme <paramref name="userId"/> (sans en-tête de dossier).</summary>
    public HttpClient ClientFor(Guid userId, bool mfa = true)
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(userId, mfa));
        return client;
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(ManagerId));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}

internal sealed class CapturingLoggerProvider(ConcurrentQueue<string> entries) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, entries);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            string values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join('|', pairs.Select(p => $"{p.Key}={p.Value}"))
                : string.Empty;
            entries.Enqueue($"[{category}] {formatter(state, exception)} {values} {exception}");
        }
    }
}
