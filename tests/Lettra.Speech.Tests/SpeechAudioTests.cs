using System.Buffers.Binary;
using Lettra.Application.Abstractions.Speech;
using Lettra.Contracts.Common;
using Lettra.Domain.Common;

namespace Lettra.Speech.Tests;

/// <summary>Lecture des WAV reçus : seul le PCM 16 bits mono 16 kHz passe, borné en durée.</summary>
public sealed class SpeechAudioTests
{
    internal static short[] Tone(double seconds, short amplitude = 8000)
    {
        short[] samples = new short[(int)(seconds * 16000)];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)(amplitude * Math.Sin(2 * Math.PI * 440 * i / 16000));
        }

        return samples;
    }

    [Fact]
    public void Un_wav_au_bon_format_est_lu_avec_sa_duree()
    {
        byte[] wav = SpeechAudio.EncodeWav(Tone(1.5));

        Result<SpeechAudio> audio = SpeechAudio.FromWav(wav, 30);

        audio.IsSuccess.Should().BeTrue();
        audio.Value.Duration.Should().Be(TimeSpan.FromSeconds(1.5));
        audio.Value.ToSamples().Should().HaveCount(24000).And.OnlyContain(s => s >= -1f && s <= 1f);
        audio.Value.IsSilent().Should().BeFalse();
    }

    [Fact]
    public void Un_enregistrement_tres_faible_est_du_silence() =>
        SpeechAudio.FromWav(SpeechAudio.EncodeWav(Tone(1, amplitude: 50)), 30).Value.IsSilent().Should().BeTrue();

    [Fact]
    public void Au_dela_de_la_duree_maximale_le_wav_est_refuse() =>
        SpeechAudio.FromWav(SpeechAudio.EncodeWav(Tone(3)), 2).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Speech.AudioTooLong);

    [Fact]
    public void La_duree_maximale_ne_depasse_jamais_le_plafond_absolu() =>
        SpeechAudio.MaxBytes(10_000).Should().Be(SpeechAudio.MaxBytes(SpeechSettings.HardMaxSeconds));

    [Theory]
    [InlineData(20, 1, 16000, 16)] // pas du PCM
    [InlineData(1, 2, 16000, 16)] // stéréo
    [InlineData(1, 1, 44100, 16)] // 44,1 kHz
    [InlineData(1, 1, 16000, 8)] // 8 bits
    public void Un_autre_format_que_pcm_16_bits_mono_16_khz_est_refuse(int format, int channels, int rate, int bits)
    {
        byte[] wav = SpeechAudio.EncodeWav(Tone(0.5));
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(20), (ushort)format);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(22), (ushort)channels);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), rate);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(34), (ushort)bits);

        SpeechAudio.FromWav(wav, 30).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Speech.AudioInvalid);
    }

    [Fact]
    public void Ce_qui_n_est_pas_un_wav_est_refuse()
    {
        SpeechAudio.FromWav(new byte[100], 30).IsFailure.Should().BeTrue();
        SpeechAudio.FromWav("ID3 un mp3 qui se fait passer pour un wav............."u8.ToArray(), 30).IsFailure.Should().BeTrue();

        byte[] headerOnly = SpeechAudio.EncodeWav([]);
        SpeechAudio.FromWav(headerOnly, 30).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Un_enregistreur_en_flux_qui_laisse_la_taille_a_zero_est_accepte()
    {
        byte[] wav = SpeechAudio.EncodeWav(Tone(1));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), 0);

        SpeechAudio.FromWav(wav, 30).Value.Duration.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Les_blocs_inconnus_avant_les_donnees_sont_sautes()
    {
        byte[] plain = SpeechAudio.EncodeWav(Tone(0.25));
        byte[] list = [.. "LIST"u8, 4, 0, 0, 0, .. "INFO"u8];
        byte[] wav = [.. plain[..36], .. list, .. plain[36..]];

        SpeechAudio.FromWav(wav, 30).Value.Duration.Should().Be(TimeSpan.FromSeconds(0.25));
    }
}

public sealed class TranscriptCleanerTests
{
    [Theory]
    [InlineData("Sous-titres réalisés par la communauté d'Amara.org", "")]
    [InlineData("[Musique]", "")]
    [InlineData(" Merci d'avoir regardé. ", "")]
    [InlineData("(rires) ouvrir les devis [bruit]", "ouvrir les devis")]
    [InlineData("Thanks for watching!", "")]
    [InlineData("ajoute deux heures de conseil", "ajoute deux heures de conseil")]
    public void Les_inventions_de_whisper_sur_le_silence_sont_retirees(string heard, string expected) =>
        TranscriptCleaner.Clean(heard).Should().Be(expected);
}
