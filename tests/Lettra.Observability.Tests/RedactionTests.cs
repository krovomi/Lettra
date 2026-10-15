using System.Diagnostics;
using Lettra.Infrastructure.Observability;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;

namespace Lettra.Observability.Tests;

public sealed class RedactionTests
{
    [Theory]
    [InlineData("secret@quenelle.fr")]
    [InlineData("FR76 3000 6000 0112 3456 7890 189")]
    [InlineData("FR96552100554")]
    [InlineData("552100554")]
    [InlineData("552 100 554")]
    [InlineData("55210055400013")]
    public void Les_valeurs_qui_ressemblent_a_une_donnee_de_dossier_sont_masquees(string value)
    {
        string scrubbed = DossierDataScrubber.Scrub($"avant {value} après");

        scrubbed.Should().Be("avant *** après");
    }

    [Theory]
    [InlineData("0199e3a0-0000-7000-8000-00000000d051")]
    [InlineData("CreateCustomerCommand")]
    [InlineData("/api/v1/customers/{id}")]
    [InlineData("Customer.SirenRequired")]
    [InlineData("12345678")]
    public void Les_identifiants_techniques_et_les_codes_restent_lisibles(string value) =>
        DossierDataScrubber.Scrub(value).Should().Be(value);

    [Fact]
    public void Un_long_texte_sans_donnee_reste_lisible_et_ne_depasse_pas_le_delai()
    {
        // Ce que journalise une migration : de longues suites d'identifiants SQL, sans « @ ».
        string sql = string.Concat(Enumerable.Repeat("UPDATE \"Customers\" SET \"SearchKey\" = REPLACE(UPPER(\"Name\"), 'É', 'E'); ", 2_000))
            + new string('a', 200_000);

        DossierDataScrubber.Scrub(sql).Should().Be(sql);
        DossierDataScrubber.LooksSensitive(sql).Should().BeFalse();
    }

    [Fact]
    public void Les_attributs_interdits_et_les_valeurs_sensibles_ne_sortent_pas_des_traces()
    {
        List<Activity> exported = [];
        using ActivitySource source = new("Lettra.Tests.Redaction");
        using (TracerProvider tracer = Sdk.CreateTracerProviderBuilder()
            .AddSource(source.Name)
            .AddProcessor(new ActivityRedactionProcessor())
            .AddInMemoryExporter(exported)
            .Build())
        {
            using Activity? activity = source.StartActivity("POST /api/v1/customers");
            activity!.SetTag("url.query", "?email=secret@quenelle.fr");
            activity.SetTag("db.statement", "INSERT INTO customers VALUES ('Fromagerie Quenelle')");
            activity.SetTag("http.request.header.x-dossier", "abc");
            activity.SetTag("lettra.error.code", "Customer.SirenRequired");
            activity.SetTag("note", "contact secret@quenelle.fr");
            activity.SetStatus(ActivityStatusCode.Error, "SIREN 552100554 refusé");
        }

        Activity span = exported.Should().ContainSingle().Subject;
        span.TagObjects.Select(t => t.Key).Should().NotContain(["url.query", "db.statement", "http.request.header.x-dossier"]);
        span.GetTagItem("lettra.error.code").Should().Be("Customer.SirenRequired");
        span.GetTagItem("note").Should().Be("contact ***");
        span.StatusDescription.Should().Be("SIREN *** refusé");
    }

    [Fact]
    public void Les_journaux_exportes_ne_portent_ni_donnee_ni_message_d_exception()
    {
        List<LogRecord> exported = [];
        using (ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = true;
            o.AddProcessor(new LogRedactionProcessor());
            o.AddInMemoryExporter(exported);
        })))
        {
            ILogger logger = factory.CreateLogger("Essai");
            InvalidOperationException error = new("violation d'unicité pour 'secret@quenelle.fr'");
#pragma warning disable CA1848, CA2254 // Test : on simule un journal mal écrit.
            logger.LogError(error, "Échec pour {Email}", "secret@quenelle.fr");
#pragma warning restore CA1848, CA2254
        }

        LogRecord record = exported.Should().ContainSingle().Subject;
        record.Exception.Should().BeNull();
        record.FormattedMessage.Should().Be("Échec pour ***");
        record.Attributes!.Should().Contain(new KeyValuePair<string, object?>(LogRedactionProcessor.ExceptionTypeAttribute, typeof(InvalidOperationException).FullName));
        record.Attributes!.Select(a => a.Value?.ToString()).Should().NotContain(v => v != null && v.Contains("quenelle", StringComparison.Ordinal));
    }
}
