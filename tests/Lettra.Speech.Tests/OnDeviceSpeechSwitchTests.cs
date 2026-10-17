using Lettra.Infrastructure.Speech.OnDevice;

namespace Lettra.Speech.Tests;

/// <summary>Interrupteur de la dictée sur le poste (Lettra:Speech:Engine) : ouverte par défaut, fermée par None.</summary>
public sealed class OnDeviceSpeechSwitchTests
{
    [Theory]
    [InlineData(null, true, true)]
    [InlineData("", true, true)]
    [InlineData(null, false, false)]
    [InlineData("OnDevice", false, true)]
    [InlineData(" ondevice ", true, true)]
    [InlineData("None", true, false)]
    [InlineData("none", true, false)]
    [InlineData("WhisperServer", true, false)]
    [InlineData("n'importe quoi", true, false)]
    public void Le_reglage_du_moteur_ouvre_ou_ferme_la_dictee(string? engine, bool enabledByDefault, bool expected) =>
        OnDeviceSpeechOptions.IsEnabled(engine, enabledByDefault).Should().Be(expected);

    [Fact]
    public void Le_reglage_a_la_meme_cle_que_les_serveurs() =>
        OnDeviceSpeechOptions.EngineKey.Should().Be(Infrastructure.Speech.SpeechOptions.SectionName + ":Engine");
}
