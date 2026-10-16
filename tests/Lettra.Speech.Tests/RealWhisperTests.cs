using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lettra.Application.Abstractions.Speech;
using Lettra.Application.Features.Speech;
using Lettra.Contracts.Speech;
using Lettra.Domain.Common;
using Lettra.Infrastructure.Speech;
using Lettra.Infrastructure.Speech.OnDevice;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Lettra.Speech.Tests;

/// <summary>
/// Le corpus de phrases face à un vrai Whisper (workflow « Voix ») : chaque enregistrement (voix de synthèse) passe
/// par le moteur, le nettoyage, la mise en forme et la grammaire, comme dans le cas d'usage. Sans
/// <c>LETTRA_VOICE_CORPUS</c> (dossier des WAV) et un moteur (<c>LETTRA_WHISPER_MODEL</c> ou
/// <c>LETTRA_WHISPER_SERVER</c>), ces tests sont sautés.
/// </summary>
public sealed class RealWhisperTests(ITestOutputHelper output)
{
    private static readonly string? CorpusFolder = Environment.GetEnvironmentVariable("LETTRA_VOICE_CORPUS");
    private static readonly string? ModelPath = Environment.GetEnvironmentVariable("LETTRA_WHISPER_MODEL");
    private static readonly string? ServerUrl = Environment.GetEnvironmentVariable("LETTRA_WHISPER_SERVER");

    [Fact]
    public void Chaque_phrase_du_corpus_donne_sa_commande_a_la_grammaire()
    {
        foreach (CorpusPhrase phrase in CorpusPhrase.Load())
        {
            VoiceCommand? command = VoiceCommandParser.Parse(phrase.Text, phrase.Language);
            phrase.Matches(command).Should().BeTrue($"« {phrase.Text} » doit donner {phrase.Expected}");
        }
    }

    [SkippableFact]
    public async Task Les_enregistrements_du_corpus_sont_au_format_de_whisper()
    {
        Skip.If(CorpusFolder is null, "LETTRA_VOICE_CORPUS non défini");
        foreach (CorpusPhrase phrase in CorpusPhrase.Load())
        {
            Result<SpeechAudio> audio = SpeechAudio.FromWav(await File.ReadAllBytesAsync(Path.Combine(CorpusFolder!, phrase.Id + ".wav")), 30);
            audio.IsSuccess.Should().BeTrue(phrase.Id);
            audio.Value.IsSilent().Should().BeFalse(phrase.Id);
        }
    }

    [SkippableFact]
    public async Task Whisper_net_sur_le_poste_comprend_le_corpus()
    {
        Skip.If(CorpusFolder is null || ModelPath is null, "LETTRA_VOICE_CORPUS et LETTRA_WHISPER_MODEL non définis");
        string sha1;
        await using (FileStream model = File.OpenRead(ModelPath!))
        {
#pragma warning disable CA5350 // Empreinte d'intégrité, comme whisper.cpp.
            sha1 = Convert.ToHexStringLower(await SHA1.HashDataAsync(model));
#pragma warning restore CA5350
        }

        OnDeviceSpeechOptions options = new() { ModelDirectory = Path.GetDirectoryName(Path.GetFullPath(ModelPath!))!, AllowDownload = false };
        using WhisperModelStore store = new(options, new NoDownloads(), NullLogger<WhisperModelStore>.Instance)
        {
            FileOverride = new WhisperModelFile(Path.GetFileName(ModelPath!), sha1),
        };
        using WhisperNetSpeechToText engine = new(store, options, NullLogger<WhisperNetSpeechToText>.Instance);

        await RunCorpusAsync(engine);
    }

    [SkippableFact]
    public async Task Le_serveur_whisper_cpp_comprend_le_corpus()
    {
        Skip.If(CorpusFolder is null || ServerUrl is null, "LETTRA_VOICE_CORPUS et LETTRA_WHISPER_SERVER non définis");
        using HttpClient http = new() { BaseAddress = new Uri(ServerUrl!.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(2) };
        WhisperServerSpeechToText engine = new(http, new WhisperServerOptions(), NullLogger<WhisperServerSpeechToText>.Instance);

        await RunCorpusAsync(engine);
    }

    private async Task RunCorpusAsync(ISpeechToText engine)
    {
        double maxWer = Threshold("LETTRA_VOICE_MAX_WER", 0.5);
        double minCommands = Threshold("LETTRA_VOICE_MIN_COMMANDS", 0.6);
        StringBuilder report = new();
        report.AppendLine(CultureInfo.InvariantCulture, $"### Voix : corpus face à `{engine.Name}`").AppendLine()
            .AppendLine("| Phrase | Lu | Entendu, mis en forme | Erreurs de mots | Commande |")
            .AppendLine("|---|---|---|---|---|");

        List<double> errors = [];
        int commands = 0;
        int commandsOk = 0;
        foreach (CorpusPhrase phrase in CorpusPhrase.Load())
        {
            byte[] wav = await File.ReadAllBytesAsync(Path.Combine(CorpusFolder!, phrase.Id + ".wav"));
            SpeechAudio audio = SpeechAudio.FromWav(wav, SpeechSettings.HardMaxSeconds).Value;
            Result<SpeechTranscript> transcript = await engine.TranscribeAsync(
                audio, new SpeechRequest(phrase.Language.ToCode(), SpeechPrompts.For(phrase.Language)), CancellationToken.None);
            transcript.IsSuccess.Should().BeTrue($"le moteur doit répondre pour {phrase.Id}");

            string heard = TranscriptCleaner.Clean(transcript.Value.Text);
            string formatted = DictationFormatter.Format(heard, phrase.Language);
            double wer = WordErrorRate(DictationFormatter.Format(phrase.Text, phrase.Language), formatted);
            errors.Add(wer);

            string verdict = "dictée";
            if (phrase.Expected is not null)
            {
                commands++;
                bool ok = phrase.Matches(VoiceCommandParser.Parse(heard, phrase.Language));
                commandsOk += ok ? 1 : 0;
                verdict = ok ? "✅ " + phrase.Expected : "❌ attendu " + phrase.Expected;
            }

            report.AppendLine(CultureInfo.InvariantCulture, $"| {phrase.Id} | {phrase.Text} | {formatted} | {wer:P0} | {verdict} |");
        }

        double meanWer = errors.Average();
        double commandRate = commands == 0 ? 1 : (double)commandsOk / commands;
        report.AppendLine().AppendLine(CultureInfo.InvariantCulture,
            $"Taux d'erreur de mots moyen : **{meanWer:P1}** (seuil {maxWer:P0}). Commandes reconnues : **{commandsOk}/{commands}** (seuil {minCommands:P0}).");
        output.WriteLine(report.ToString());
        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
        {
            await File.AppendAllTextAsync(summary, report.ToString());
        }

        meanWer.Should().BeLessThanOrEqualTo(maxWer);
        commandRate.Should().BeGreaterThanOrEqualTo(minCommands);
    }

    private static double Threshold(string name, double fallback) =>
        double.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : fallback;

    /// <summary>Distance d'édition en mots, rapportée au nombre de mots attendus ; accents et ponctuation ignorés.</summary>
    internal static double WordErrorRate(string expected, string actual)
    {
        string[] e = Words(expected);
        string[] a = Words(actual);
        if (e.Length == 0)
        {
            return a.Length == 0 ? 0 : 1;
        }

        int[,] d = new int[e.Length + 1, a.Length + 1];
        for (int i = 0; i <= e.Length; i++)
        {
            d[i, 0] = i;
        }

        for (int j = 0; j <= a.Length; j++)
        {
            d[0, j] = j;
        }

        for (int i = 1; i <= e.Length; i++)
        {
            for (int j = 1; j <= a.Length; j++)
            {
                int cost = string.Equals(e[i - 1], a[j - 1], StringComparison.Ordinal) ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }

        return (double)d[e.Length, a.Length] / e.Length;
    }

    private static string[] Words(string text)
    {
        StringBuilder plain = new();
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            plain.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        return plain.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private sealed class NoDownloads : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Aucun téléchargement dans ce test.");
    }
}

/// <summary>Une phrase du corpus (<c>Corpus/phrases.json</c>).</summary>
internal sealed record CorpusPhrase(string Id, SpokenLanguage Language, string Text, CorpusPhrase.Command? Expected)
{
    public static IReadOnlyList<CorpusPhrase> Load()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Corpus", "phrases.json")));
        return [.. document.RootElement.GetProperty("phrases").EnumerateArray().Select(p => new CorpusPhrase(
            p.GetProperty("id").GetString()!,
            SpokenLanguages.FromCode(p.GetProperty("language").GetString()) ?? throw new InvalidDataException("langue"),
            p.GetProperty("text").GetString()!,
            p.TryGetProperty("command", out JsonElement c)
                ? new Command(
                    Enum.Parse<VoiceCommandKind>(c.GetProperty("kind").GetString()!),
                    c.TryGetProperty("target", out JsonElement t) ? Enum.Parse<VoiceTarget>(t.GetString()!) : null,
                    c.TryGetProperty("filter", out JsonElement f) ? Enum.Parse<VoiceFilter>(f.GetString()!) : null)
                : null))];
    }

    /// <summary>Une dictée ne doit déclencher aucune commande ; une commande doit avoir le bon type, la bonne cible et le bon filtre.</summary>
    public bool Matches(VoiceCommand? command) => Expected is null
        ? command is null
        : command is not null && command.Kind == Expected.Kind
          && (Expected.Target is null || command.Target == Expected.Target)
          && (Expected.Filter is null || command.Filter == Expected.Filter);

    internal sealed record Command(VoiceCommandKind Kind, VoiceTarget? Target, VoiceFilter? Filter)
    {
        public override string ToString() => string.Join(' ', new object?[] { Kind, Target, Filter }.OfType<object>());
    }
}
