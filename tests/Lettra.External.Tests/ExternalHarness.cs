using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Lettra.Application.Abstractions.External;
using Lettra.Infrastructure.External;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Lettra.External.Tests;

/// <summary>Serveur HTTP simulé : répond selon le chemin et garde la trace des requêtes reçues.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    public ConcurrentQueue<(Uri Uri, string? Body)> Requests { get; } = new();

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue((request.RequestUri!, body));
        return Respond(request);
    }
}

/// <summary>Monte les vrais adaptateurs (cache et résilience compris) sur le serveur simulé.</summary>
internal sealed class ExternalHarness : IDisposable
{
    private readonly ServiceProvider _services;

    public ExternalHarness(bool enabled = true)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lettra:External:Enabled"] = enabled ? "true" : "false",
                ["Lettra:External:Timeout"] = "00:00:02",
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(new CapturingLoggerProvider(Logs)));
        services.AddExternalServices(configuration);
        services.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = Handler));
        _services = services.BuildServiceProvider();
    }

    public FakeHttpHandler Handler { get; } = new();

    public ConcurrentQueue<string> Logs { get; } = new();

    public ICompanyDirectory Directory => _services.GetRequiredService<ICompanyDirectory>();

    public IVatNumberChecker Vies => _services.GetRequiredService<IVatNumberChecker>();

    public void Dispose() => _services.Dispose();
}

internal sealed class CapturingLoggerProvider(ConcurrentQueue<string> entries) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Logger(entries);

    public void Dispose()
    {
    }

    private sealed class Logger(ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(formatter(state, exception) + " " + exception);
    }
}
