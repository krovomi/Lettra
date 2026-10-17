using Lettra.UI.Shared.Components.Palette;
using Lettra.UI.Shared.Localization;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Voice;

/// <summary>Palette Ctrl+K : « Parler à Lettra » et le choix d'afficher ou de masquer les micros des champs.</summary>
public sealed class VoicePaletteProvider(VoiceInput voice, IStringLocalizer<SharedResource> localizer) : ICommandPaletteProvider
{
    public Task<IReadOnlyList<PaletteItem>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        string group = localizer["Palette.Group.Voice"];
        List<PaletteItem> items =
        [
            new(group, localizer["Voice.Palette.Speak"], () =>
            {
                voice.OpenPanel();
                return Task.CompletedTask;
            }, "F8"),
        ];
        if (voice.IsReady)
        {
            items.Add(new(group, localizer[voice.Enabled ? "Voice.Palette.HideMics" : "Voice.Palette.ShowMics"], () => voice.SetEnabledAsync(!voice.Enabled)));
        }

        return Task.FromResult<IReadOnlyList<PaletteItem>>([.. items.Where(i => PaletteMatcher.Score(i.Label, query) > 0)]);
    }
}
