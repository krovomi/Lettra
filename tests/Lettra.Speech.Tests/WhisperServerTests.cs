using System.Net;
using System.Text;
using Lettra.Application.Abstractions.Speech;
using Lettra.Contracts.Common;
using Lettra.Domain.Common;
using Lettra.Infrastructure.Speech;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lettra.Speech.Tests;

/// <summary>Serveur whisper.cpp de la cellule : requête multipart, réponse JSON, et refus de tout point d'accès public.</summary>
public sealed class WhisperServerTests
{
    private static readonly SpeechRequest French = new("fr", "Devis, facture");

    [Theory]
    [InlineData("http://whisper:8080/")]
    [InlineData("http://whisper.lettra.svc:8080/")]
    [InlineData("http://whisper.lettra.svc.cluster.local/")]
    [InlineData("http://voix.cabinet.lan/")]
    [InlineData("http://localhost:8080/")]
    [InlineData("http://127.0.0.1:8080/")]
    [InlineData("http://10.2.3.4/")]
    [InlineData("http://172.20.0.5/")]
    [InlineData("http://192.168.1.20/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[fd12::5]/")]
    public void Un_point_d_acces_de_la_cellule_ou_du_cabinet_est_interne(string endpoint) =>
        InternalEndpoint.IsInternal(new Uri(endpoint)).Should().BeTrue();

    [Theory]
    [InlineData("https://api.openai.com/v1/")]
    [InlineData("https://whisper.example.com/")]
    [InlineData("http://8.8.8.8/")]
    [InlineData("http://172.32.0.1/")]
    [InlineData("http://[2001:db8::1]/")]
    public void Un_point_d_acces_public_n_est_pas_interne(string endpoint) =>
        InternalEndpoint.IsInternal(new Uri(endpoint)).Should().BeFalse();

    [Fact]
    public void Sans_moteur_la_voix_reste_desactivee()
    {
        using ServiceProvider services = Build([]);

        services.GetService<SpeechSettings>().Should().BeNull("l'application garde son réglage par défaut, désactivé");
    }

    [Fact]
    public void Un_serveur_whisper_public_est_refuse_au_demarrage()
    {
        Action start = () => Build(new() { ["Lettra:Speech:Engine"] = "WhisperServer", ["Lettra:Speech:WhisperServer:Endpoint"] = "https://whisper.example.com/" });

        start.Should().Throw<InvalidOperationException>().WithMessage("*ADR-0012*");
    }

    [Fact]
    public void Un_serveur_whisper_sans_adresse_est_refuse_au_demarrage()
    {
        Action start = () => Build(new() { ["Lettra:Speech:Engine"] = "WhisperServer" });

        start.Should().Throw<InvalidOperationException>().WithMessage("*Endpoint*");
    }

    [Fact]
    public void Un_serveur_whisper_de_la_cellule_active_la_voix_dans_les_langues_prises_en_charge()
    {
        using ServiceProvider services = Build(new()
        {
            ["Lettra:Speech:Engine"] = "WhisperServer",
            ["Lettra:Speech:WhisperServer:Endpoint"] = "http://whisper:8080",
            ["Lettra:Speech:Languages:0"] = "fr",
            ["Lettra:Speech:Languages:1"] = "de",
            ["Lettra:Speech:MaxSeconds"] = "500",
        });

        services.GetRequiredService<SpeechSettings>().Should().BeEquivalentTo(
            new SpeechSettings(true, "whisper-serveur", false, ["fr"], SpeechSettings.HardMaxSeconds));
        services.GetRequiredService<ISpeechToText>().Name.Should().Be("whisper-serveur");
    }

    [Fact]
    public async Task La_requete_suit_le_format_de_whisper_cpp_et_la_reponse_est_lue()
    {
        RecordingHandler handler = new(HttpStatusCode.OK, """{"text":" Ouvrir les devis."}""");
        WhisperServerSpeechToText engine = Engine(handler);

        Result<SpeechTranscript> result = await engine.TranscribeAsync(Audio(), French, CancellationToken.None);

        result.Value.Text.Should().Be(" Ouvrir les devis.");
        handler.Uri!.AbsoluteUri.Should().Be("http://whisper:8080/inference");
        handler.Body.Should().Contain("name=file").And.Contain("filename=dictee.wav")
            .And.Contain("name=language").And.Contain("Devis, facture")
            .And.Contain("name=response_format").And.Contain("json");
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "")]
    [InlineData(HttpStatusCode.OK, "pas du json")]
    [InlineData(HttpStatusCode.OK, "{}")]
    public async Task Un_serveur_en_panne_rend_moteur_indisponible(HttpStatusCode status, string body)
    {
        WhisperServerSpeechToText engine = Engine(new RecordingHandler(status, body));

        Result<SpeechTranscript> result = await engine.TranscribeAsync(Audio(), French, CancellationToken.None);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Speech.EngineUnavailable);
    }

    [Fact]
    public async Task Un_serveur_injoignable_rend_moteur_indisponible()
    {
        WhisperServerSpeechToText engine = Engine(new RecordingHandler(new HttpRequestException("refusé")));

        Result<SpeechTranscript> result = await engine.TranscribeAsync(Audio(), French, CancellationToken.None);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Speech.EngineUnavailable);
    }

    private static SpeechAudio Audio() => SpeechAudio.FromWav(SpeechAudio.EncodeWav(SpeechAudioTests.Tone(0.5)), 30).Value;

    private static WhisperServerSpeechToText Engine(RecordingHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("http://whisper:8080/") },
        new WhisperServerOptions(),
        NullLogger<WhisperServerSpeechToText>.Instance);

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSpeech(configuration);
        return services.BuildServiceProvider();
    }

    internal sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        private readonly Exception? _failure;

        public RecordingHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public RecordingHandler(Exception failure)
            : this(HttpStatusCode.OK, string.Empty) => _failure = failure;

        public Uri? Uri { get; private set; }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            if (request.Content is not null)
            {
                Body = Encoding.UTF8.GetString(await request.Content.ReadAsByteArrayAsync(cancellationToken));
            }

            if (_failure is not null)
            {
                throw _failure;
            }

            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }
}
