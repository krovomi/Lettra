using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lettra.Api.Tests;

/// <summary>L'API en mode 2 sur un dossier SQLite temporaire, avec capture de tous les journaux (niveau Trace).</summary>
public sealed class LettraApiFactory : WebApplicationFactory<Program>
{
    public static readonly Guid DossierId = new("0199e3a0-0000-7000-8000-00000000a001");

    private readonly string _folder = Directory.CreateTempSubdirectory("lettra-api-").FullName;

    public ConcurrentQueue<string> Logs { get; } = new();

    public static readonly Guid OrganisationId = new("0199e3a0-0000-7000-8000-0000000000c1");

    public static Dictionary<string, string?> DossierSettings(string database) => new()
    {
        ["ConnectionStrings:Catalog"] = $"Data Source={database}.catalog;Pooling=False",
        ["Lettra:Dossiers:0:Id"] = DossierId.ToString(),
        ["Lettra:Dossiers:0:OrganisationId"] = OrganisationId.ToString(),
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
            config.AddInMemoryCollection(settings);
        });
        builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(new CapturingLoggerProvider(Logs));
        });
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
