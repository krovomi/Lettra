using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Lettra.Application.Abstractions.Speech;
using Lettra.Application.Features.Speech;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.Contracts.Speech;
using Lettra.Domain.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Api.Tests;

/// <summary>
/// La voix par l'API (vague 5, lane D) : désactivée par défaut, puis une transcription mise en forme et, en mode
/// commande, la commande reconnue. Le moteur est remplacé par un faux : le vrai Whisper tourne dans le workflow « Voix ».
/// </summary>
public sealed class SpeechApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static readonly SpeechSettings Enabled = new(true, "test:voix", false, ["fr", "en"], 30);

    [Fact]
    public async Task Sans_moteur_choisi_la_voix_est_desactivee()
    {
        using HttpClient client = Client(factory);

        SpeechStatusResponse status = (await client.GetFromJsonAsync("/api/v1/voix/statut", ContractsJsonContext.Default.SpeechStatusResponse))!;
        using HttpResponseMessage response = await PostAsync(client, Tone(), "mode=Command&langue=fr");

        status.Enabled.Should().BeFalse();
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemAsync(response)).Code.Should().Be(ErrorCodes.Speech.Disabled);
    }

    [Fact]
    public async Task En_mode_commande_la_reponse_porte_le_texte_et_la_commande()
    {
        FakeEngine engine = new(" ouvrir les factures en retard");
        using WebApplicationFactory<Program> enabled = WithEngine(engine);
        using HttpClient client = Client(enabled);

        SpeechStatusResponse status = (await client.GetFromJsonAsync("/api/v1/voix/statut", ContractsJsonContext.Default.SpeechStatusResponse))!;
        using HttpResponseMessage response = await PostAsync(client, Tone(), "mode=Command&langue=fr");

        status.Should().BeEquivalentTo(new SpeechStatusResponse(true, "test:voix", false, ["fr", "en"], 30));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        TranscriptionResponse transcription = (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.TranscriptionResponse))!;
        transcription.Text.Should().Be("Ouvrir les factures en retard");
        transcription.Language.Should().Be("fr");
        transcription.Command.Should().NotBeNull();
        transcription.Command!.Kind.Should().Be(VoiceCommandKind.Navigate);
        transcription.Command.Target.Should().Be(VoiceTarget.Invoices);
        transcription.Command.Filter.Should().Be(VoiceFilter.Overdue);
        engine.LastRequest!.Language.Should().Be("fr");
        engine.LastRequest.Prompt.Should().Contain("Devis", "le vocabulaire du métier oriente le moteur").And.Contain("TVA");
    }

    [Fact]
    public async Task En_dictee_le_texte_est_mis_en_forme_sans_commande()
    {
        using WebApplicationFactory<Program> enabled = WithEngine(new FakeEngine("remise de dix pour cent virgule merci point"));
        using HttpClient client = Client(enabled);

        using HttpResponseMessage response = await PostAsync(client, Tone(), "langue=fr");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        TranscriptionResponse transcription = (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.TranscriptionResponse))!;
        transcription.Text.Should().Be("Remise de 10 %, merci.");
        transcription.Command.Should().BeNull();
    }

    [Fact]
    public async Task Un_corps_qui_n_est_pas_un_wav_est_refuse()
    {
        using WebApplicationFactory<Program> enabled = WithEngine(new FakeEngine("bonjour"));
        using HttpClient client = Client(enabled);

        using ByteArrayContent mp3 = new([1, 2, 3, 4]);
        mp3.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        using HttpResponseMessage wrongType = await client.PostAsync(new Uri("/api/v1/voix/transcription", UriKind.Relative), mp3);
        using HttpResponseMessage notWav = await PostAsync(client, new byte[64], "langue=fr");

        wrongType.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType, "la route n'accepte que le WAV");
        notWav.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemAsync(notWav)).Code.Should().Be(ErrorCodes.Speech.AudioInvalid);
    }

    [Theory]
    [InlineData("audio/x-wav")]
    [InlineData("audio/wave")]
    public async Task Les_autres_noms_du_wav_sont_acceptes(string contentType)
    {
        using WebApplicationFactory<Program> enabled = WithEngine(new FakeEngine("bonjour"));
        using HttpClient client = Client(enabled);

        using ByteArrayContent content = new(Tone());
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using HttpResponseMessage response = await client.PostAsync(new Uri("/api/v1/voix/transcription?langue=fr", UriKind.Relative), content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Un_enregistrement_trop_long_ou_silencieux_ne_reveille_pas_le_moteur()
    {
        FakeEngine engine = new("bonjour");
        using WebApplicationFactory<Program> enabled = WithEngine(engine);
        using HttpClient client = Client(enabled);

        using HttpResponseMessage tooLong = await PostAsync(client, Tone(seconds: 31), "langue=fr");
        using HttpResponseMessage silent = await PostAsync(client, SpeechAudio.EncodeWav(new short[16000]), "langue=fr");
        using HttpResponseMessage german = await PostAsync(client, Tone(), "langue=de");

        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemAsync(tooLong)).Code.Should().Be(ErrorCodes.Speech.AudioTooLong);
        silent.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemAsync(silent)).Code.Should().Be(ErrorCodes.Speech.NothingHeard);
        german.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        engine.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Un_moteur_arrete_rend_503_avec_son_code()
    {
        using WebApplicationFactory<Program> enabled = WithEngine(new FakeEngine(null));
        using HttpClient client = Client(enabled);

        using HttpResponseMessage response = await PostAsync(client, Tone(), "langue=fr");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ProblemAsync(response)).Code.Should().Be(ErrorCodes.Speech.EngineUnavailable);
    }

    [Fact]
    public async Task Dans_la_demo_un_serveur_whisper_pas_encore_pret_rend_503_sans_bloquer_l_api()
    {
        // Comme deploy/demo/compose.yaml pendant le téléchargement du modèle : moteur WhisperServer configuré, serveur
        // qui ne répond pas encore (port fermé sur la boucle locale, adresse interne acceptée par le garde-fou).
        using WebApplicationFactory<Program> demo = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Lettra:Speech:Engine", "WhisperServer")
            .UseSetting("Lettra:Speech:WhisperServer:Endpoint", "http://127.0.0.1:9/")
            .UseSetting("Lettra:Speech:WhisperServer:Timeout", "00:00:05"));
        using HttpClient client = Client(demo);

        using HttpResponseMessage health = await client.GetAsync(new Uri("/health", UriKind.Relative));
        SpeechStatusResponse status = (await client.GetFromJsonAsync("/api/v1/voix/statut", ContractsJsonContext.Default.SpeechStatusResponse))!;
        using HttpResponseMessage response = await PostAsync(client, Tone(), "langue=fr");

        health.StatusCode.Should().Be(HttpStatusCode.OK);
        status.Enabled.Should().BeTrue();
        status.Engine.Should().Be("whisper-serveur");
        status.OnDevice.Should().BeFalse();
        status.Ready.Should().BeFalse("l'interface dit que le moteur se prépare au lieu de laisser parler pour rien");
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ProblemAsync(response)).Code.Should().Be(ErrorCodes.Speech.EngineUnavailable);
    }

    [Fact]
    public async Task Le_texte_dicte_n_apparait_jamais_dans_les_journaux()
    {
        const string secret = "Boulangerie Zéphyrine";
        using WebApplicationFactory<Program> enabled = WithEngine(new FakeEngine($"devis pour {secret}"));
        using HttpClient client = Client(enabled);

        using HttpResponseMessage response = await PostAsync(client, Tone(), "mode=Command&langue=fr");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Logs.Should().NotContain(l => l.Contains("Zéphyrine", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task La_politique_des_permissions_ouvre_le_micro_a_la_seule_origine_de_l_application()
    {
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        string policy = string.Join(", ", response.Headers.GetValues("Permissions-Policy"));
        policy.Should().Contain("microphone=(self)").And.Contain("camera=()");
    }

    /// <summary>Un son de 440 Hz : assez fort pour ne pas passer pour du silence.</summary>
    private static byte[] Tone(double seconds = 1)
    {
        short[] samples = new short[(int)(seconds * SpeechStatusResponse.SampleRate)];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)(8000 * Math.Sin(2 * Math.PI * 440 * i / SpeechStatusResponse.SampleRate));
        }

        return SpeechAudio.EncodeWav(samples);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, byte[] wav, string query)
    {
        using ByteArrayContent content = new(wav);
        content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        return await client.PostAsync(new Uri($"/api/v1/voix/transcription?{query}", UriKind.Relative), content);
    }

    private static async Task<ProblemResponse> ProblemAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.ProblemResponse))!;

    private WebApplicationFactory<Program> WithEngine(ISpeechToText engine) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton(Enabled));
            services.Replace(ServiceDescriptor.Singleton(engine));
        }));

    private static HttpClient Client(WebApplicationFactory<Program> factory)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }

    /// <summary>Faux moteur : rend le texte donné, ou « moteur indisponible » quand il n'y en a pas.</summary>
    private sealed class FakeEngine(string? text) : ISpeechToText
    {
        public int Calls { get; private set; }

        public SpeechRequest? LastRequest { get; private set; }

        public string Name => "test:voix";

        public Task<Result<SpeechTranscript>> TranscribeAsync(SpeechAudio audio, SpeechRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            Result<SpeechTranscript> result = text is null
                ? SpeechErrors.EngineUnavailable
                : new SpeechTranscript(text);
            return Task.FromResult(result);
        }
    }
}
