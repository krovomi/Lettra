using System.Buffers.Binary;
using Lettra.Contracts.Common;
using Lettra.Contracts.Speech;
using Lettra.Domain.Common;

namespace Lettra.Application.Abstractions.Speech;

/// <summary>
/// Port de la reconnaissance vocale (annexe A §7 point 12). Les adaptateurs transcrivent <b>sans quitter</b> le poste
/// (Whisper.net, MAUI) ou la cellule (serveur whisper.cpp, navigateur) : aucun service vocal tiers (ADR-0011,
/// ADR-0012). Un adaptateur ne journalise jamais le texte entendu, qui est une donnée de dossier.
/// </summary>
public interface ISpeechToText
{
    /// <summary>Nom technique du moteur (<c>whisper-serveur</c>, <c>whisper-poste</c>), repris par l'écran d'état.</summary>
    string Name { get; }

    Task<Result<SpeechTranscript>> TranscribeAsync(SpeechAudio audio, SpeechRequest request, CancellationToken cancellationToken);
}

/// <param name="Language">Code ISO 639-1 (<c>fr</c>, <c>en</c>) : jamais de détection automatique, qui se trompe sur des phrases courtes.</param>
/// <param name="Prompt">Vocabulaire du métier donné au moteur (« devis, facture, HT, TTC… ») pour l'orienter.</param>
public sealed record SpeechRequest(string Language, string Prompt);

/// <summary>Texte brut reconnu par le moteur, avant mise en forme.</summary>
public sealed record SpeechTranscript(string Text);

/// <summary>
/// Réglages vocaux du déploiement. La reconnaissance est <b>désactivée</b> par défaut : il faut choisir un moteur
/// (<c>Lettra:Speech:Engine</c>) pour l'ouvrir, comme l'assistant IA (annexe F §10).
/// </summary>
public sealed record SpeechSettings(bool Enabled, string? EngineName, bool OnDevice, IReadOnlyList<string> Languages, int MaxSeconds)
{
    /// <summary>Durée maximale acceptée, quel que soit le réglage : au-delà, une dictée se découpe.</summary>
    public const int HardMaxSeconds = 120;

    public static SpeechSettings Disabled { get; } = new(false, null, false, [], 0);

    public SpeechStatusResponse ToResponse() =>
        Enabled ? new SpeechStatusResponse(true, EngineName, OnDevice, Languages, MaxSeconds) : SpeechStatusResponse.Disabled;
}

/// <summary>
/// Audio reçu, vérifié : WAV PCM 16 bits, mono, 16 kHz (le format de Whisper, produit par l'enregistreur de l'interface).
/// L'audio n'est ni enregistré ni journalisé : il ne vit que le temps de la requête.
/// </summary>
public sealed class SpeechAudio
{
    private const int BytesPerSecond = SpeechStatusResponse.SampleRate * 2;

    /// <summary>Sous ce niveau moyen (environ -46 dBFS), l'enregistrement est du silence : inutile de réveiller le moteur.</summary>
    private const double SilenceRms = 0.005;

    private readonly ReadOnlyMemory<byte> _pcm;

    private SpeechAudio(ReadOnlyMemory<byte> wav, ReadOnlyMemory<byte> pcm)
    {
        Wav = wav;
        _pcm = pcm;
        Duration = TimeSpan.FromSeconds((double)pcm.Length / BytesPerSecond);
    }

    /// <summary>Le fichier WAV complet, tel que reçu.</summary>
    public ReadOnlyMemory<byte> Wav { get; }

    public TimeSpan Duration { get; }

    /// <summary>Plafond de taille d'un WAV accepté pour une durée donnée (en-tête compris).</summary>
    public static int MaxBytes(int maxSeconds) => (Math.Min(maxSeconds, SpeechSettings.HardMaxSeconds) * BytesPerSecond) + 4096;

    public static Result<SpeechAudio> FromWav(ReadOnlyMemory<byte> wav, int maxSeconds)
    {
        ReadOnlySpan<byte> data = wav.Span;
        if (data.Length < 44 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            return Error.Validation(ErrorCodes.Speech.AudioInvalid);
        }

        bool formatOk = false;
        int offset = 12;
        while (offset + 8 <= data.Length)
        {
            ReadOnlySpan<byte> id = data.Slice(offset, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 4, 4));
            int body = offset + 8;
            if (size < 0 || (body + size > data.Length && !id.SequenceEqual("data"u8)))
            {
                return Error.Validation(ErrorCodes.Speech.AudioInvalid);
            }

            if (id.SequenceEqual("fmt "u8))
            {
                if (size < 16)
                {
                    return Error.Validation(ErrorCodes.Speech.AudioInvalid);
                }

                ReadOnlySpan<byte> fmt = data.Slice(body, 16);
                formatOk = BinaryPrimitives.ReadUInt16LittleEndian(fmt) == 1 // PCM
                           && BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]) == 1 // mono
                           && BinaryPrimitives.ReadInt32LittleEndian(fmt[4..]) == SpeechStatusResponse.SampleRate
                           && BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]) == 16;
                if (!formatOk)
                {
                    return Error.Validation(ErrorCodes.Speech.AudioInvalid);
                }
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (!formatOk)
                {
                    return Error.Validation(ErrorCodes.Speech.AudioInvalid);
                }

                // Un enregistreur en flux peut laisser la taille à 0 ou au maximum : on prend ce qui a été reçu.
                int length = size == 0 || body + size > data.Length ? data.Length - body : size;
                length -= length % 2;
                if (length <= 0)
                {
                    return Error.Validation(ErrorCodes.Speech.AudioInvalid);
                }

                if (length > Math.Min(maxSeconds, SpeechSettings.HardMaxSeconds) * BytesPerSecond)
                {
                    return Error.Validation(ErrorCodes.Speech.AudioTooLong);
                }

                return new SpeechAudio(wav, wav.Slice(body, length));
            }

            offset = body + size + (size % 2);
        }

        return Error.Validation(ErrorCodes.Speech.AudioInvalid);
    }

    /// <summary>Échantillons normalisés entre -1 et 1, l'entrée des moteurs Whisper embarqués.</summary>
    public float[] ToSamples()
    {
        ReadOnlySpan<byte> pcm = _pcm.Span;
        float[] samples = new float[pcm.Length / 2];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(i * 2, 2)) / 32768f;
        }

        return samples;
    }

    /// <summary>Vrai si l'enregistrement ne contient que du silence ou un bruit très faible.</summary>
    public bool IsSilent()
    {
        ReadOnlySpan<byte> pcm = _pcm.Span;
        int count = pcm.Length / 2;
        if (count == 0)
        {
            return true;
        }

        double sum = 0;
        for (int i = 0; i < count; i++)
        {
            double sample = BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(i * 2, 2)) / 32768d;
            sum += sample * sample;
        }

        return Math.Sqrt(sum / count) < SilenceRms;
    }

    /// <summary>Écrit un WAV PCM 16 bits mono 16 kHz : pour les tests et pour les adaptateurs qui ré-encodent.</summary>
    public static byte[] EncodeWav(ReadOnlySpan<short> samples)
    {
        byte[] wav = new byte[44 + (samples.Length * 2)];
        Span<byte> span = wav;
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], wav.Length - 8);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteUInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], SpeechStatusResponse.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], BytesPerSecond);
        BinaryPrimitives.WriteUInt16LittleEndian(span[32..], 2);
        BinaryPrimitives.WriteUInt16LittleEndian(span[34..], 16);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], samples.Length * 2);
        for (int i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(span[(44 + (i * 2))..], samples[i]);
        }

        return wav;
    }
}

/// <summary>Moteur par défaut tant que le déploiement n'en a pas choisi : la voix est désactivée.</summary>
internal sealed class UnavailableSpeechToText : ISpeechToText
{
    public string Name => "aucun";

    public Task<Result<SpeechTranscript>> TranscribeAsync(SpeechAudio audio, SpeechRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<Result<SpeechTranscript>>(new Error(ErrorCodes.Speech.Disabled, ErrorKind.Forbidden));
}
