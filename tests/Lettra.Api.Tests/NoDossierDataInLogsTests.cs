using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Lettra.Api.Tests;

/// <summary>
/// Test de CI de l'annexe G : aucune donnée de dossier dans les journaux techniques, à tous les niveaux
/// (y compris les traces SQL d'EF Core), sur le parcours nominal comme sur les refus ; et rien non plus dans ce que
/// l'export OTLP enverrait au collecteur (traces, métriques, journaux), recherche dans l'adresse comprise.
/// </summary>
public sealed class NoDossierDataInLogsTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static readonly string[] DossierData =
        ["Fromagerie", "Quenelle", "552100554", "55210055400013", "55210055400014", "FR96552100554", "secret", "quai Saint-Antoine", "69002", "Lyon"];

    private static readonly CreateCustomerRequest Valid = new()
    {
        Name = "Fromagerie Quenelle",
        Siret = "55210055400013",
        VatNumber = "FR96552100554",
        Email = "secret@quenelle.fr",
        AddressLine1 = "12 quai Saint-Antoine",
        PostalCode = "69002",
        City = "Lyon",
    };

    [Fact]
    public async Task Aucune_donnee_saisie_n_apparait_dans_les_journaux()
    {
        using HttpClient client = factory.CreateClient();
        await RunCustomerJourneyAsync(client);

        factory.Logs.Should().Contain(l => l.Contains("CreateCustomerCommand", StringComparison.Ordinal), "le cas d'usage est bien journalisé");
        factory.Logs.Should().AllSatisfy(entry => entry.Should().NotContainAny(DossierData));
    }

    [Fact]
    public async Task Aucune_donnee_saisie_ne_part_dans_les_signaux_exportes()
    {
        // Mêmes processeurs que l'export OTLP de la cellule : l'exportateur en mémoire est ajouté après eux, comme
        // UseOtlpExporter, et voit donc exactement ce que le collecteur recevrait.
        LockedList<Activity> spans = new();
        LockedList<LogRecord> logs = new();
        LockedList<MetricSnapshot> metrics = new();
        using WebApplicationFactory<Program> exporting = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services
            .ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(spans))
            .ConfigureOpenTelemetryMeterProvider(meters => meters.AddInMemoryExporter(metrics))
            .ConfigureOpenTelemetryLoggerProvider(logging => logging.AddInMemoryExporter(logs))));

        using (HttpClient client = exporting.CreateClient())
        {
            await RunCustomerJourneyAsync(client);
        }

        exporting.Services.GetRequiredService<TracerProvider>().ForceFlush().Should().BeTrue();
        exporting.Services.GetRequiredService<MeterProvider>().ForceFlush().Should().BeTrue();
        exporting.Services.GetRequiredService<LoggerProvider>().ForceFlush().Should().BeTrue();

        // Le parcours a bien produit des signaux : serveur HTTP, cas d'usage, durées et journaux.
        Activity[] exportedSpans = spans.Snapshot();
        exportedSpans.Should().Contain(s => s.Kind == ActivityKind.Server && Equals(s.GetTagItem("url.path"), "/api/v1/customers"));
        exportedSpans.Should().Contain(s => s.DisplayName == "CreateCustomerCommand");
        metrics.Snapshot().Select(m => m.Name).Should().Contain(["http.server.request.duration", "lettra.use_case.count"]);
        logs.Snapshot().Should().NotBeEmpty();

        // La recherche citait le nom du client : ni la requête ni le corps ne sortent, et aucun SQL n'est tracé.
        exportedSpans.SelectMany(s => s.TagObjects).Select(t => t.Key).Should()
            .NotContain(["url.query", "db.statement", "db.query.text", "http.request.body", "client.address"]);
        exportedSpans.Select(s => s.GetTagItem("url.full")).OfType<string>().Should().NotContain(url => url.Contains('?', StringComparison.Ordinal));

        foreach (Activity span in exportedSpans)
        {
            Describe(span).Should().NotContainAny(DossierData, "span {0}", span.DisplayName);

            // Une exception tracée (appel sortant en échec, par exemple) ne garde que son type.
            span.Events.Where(e => e.Name == "exception").SelectMany(e => e.Tags)
                .Where(t => t.Key is "exception.message" or "exception.stacktrace" && !Equals(t.Value, "***"))
                .Should().BeEmpty("span {0}", span.DisplayName);
        }

        foreach (MetricSnapshot metric in metrics.Snapshot())
        {
            Describe(metric).Should().NotContainAny(DossierData, "métrique {0}", metric.Name);
        }

        foreach (LogRecord record in logs.Snapshot())
        {
            record.Exception.Should().BeNull("seul le type de l'exception sort");
            Describe(record).Should().NotContainAny(DossierData, "journal {0}", record.CategoryName);
        }
    }

    /// <summary>Création valide, création refusée (SIRET et e-mail faux), liste, puis recherches par nom et par SIREN.</summary>
    private static async Task RunCustomerJourneyAsync(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        CreateCustomerRequest invalid = Valid with { Siret = "55210055400014", Email = "secret-invalide@quenelle" };

        using HttpResponseMessage created = await client.PostAsJsonAsync("/api/v1/customers", Valid, ContractsJsonContext.Default.CreateCustomerRequest);
        created.EnsureSuccessStatusCode();
        using HttpResponseMessage refused = await client.PostAsJsonAsync("/api/v1/customers", invalid, ContractsJsonContext.Default.CreateCustomerRequest);
        using HttpResponseMessage listed = await client.GetAsync(new Uri("/api/v1/customers", UriKind.Relative));
        using HttpResponseMessage byName = await client.GetAsync(new Uri("/api/v1/customers?search=Fromagerie%20Quenelle", UriKind.Relative));
        using HttpResponseMessage bySiren = await client.GetAsync(new Uri("/api/v1/customers?search=552100554&page=1", UriKind.Relative));
        byName.EnsureSuccessStatusCode();
        bySiren.EnsureSuccessStatusCode();
    }

    private static string Describe(Activity span) => string.Join(
        '|',
        new[] { span.DisplayName, span.OperationName, span.StatusDescription ?? string.Empty }
            .Concat(span.TagObjects.Select(t => $"{t.Key}={t.Value}"))
            .Concat(span.Events.SelectMany(e => e.Tags.Select(t => $"{e.Name}:{t.Key}={t.Value}"))));

    private static string Describe(MetricSnapshot metric)
    {
        List<string> parts = [metric.Name, metric.Description, metric.Unit];
        foreach (MetricPoint point in metric.MetricPoints)
        {
            foreach (KeyValuePair<string, object?> tag in point.Tags)
            {
                parts.Add($"{tag.Key}={tag.Value}");
            }
        }

        return string.Join('|', parts);
    }

    private static string Describe(LogRecord record) => string.Join(
        '|',
        new[] { record.CategoryName ?? string.Empty, record.FormattedMessage ?? string.Empty, record.Body ?? string.Empty }
            .Concat((record.Attributes ?? []).Select(a => $"{a.Key}={a.Value}")));

    /// <summary>Liste que l'exportateur remplit depuis n'importe quel fil (journaux des services de fond compris).</summary>
    private sealed class LockedList<T> : Collection<T>
    {
        private readonly Lock _gate = new();

        public T[] Snapshot()
        {
            lock (_gate)
            {
                return [.. Items];
            }
        }

        protected override void InsertItem(int index, T item)
        {
            lock (_gate)
            {
                base.InsertItem(index, item);
            }
        }
    }
}
