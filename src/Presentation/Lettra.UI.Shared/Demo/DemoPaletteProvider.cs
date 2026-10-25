using Lettra.UI.Shared.Components.Palette;
using Lettra.UI.Shared.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Demo;

/// <summary>Palette Ctrl+K : « Démo commentée », et « Arrêter la démo commentée » pendant la lecture.</summary>
public sealed class DemoPaletteProvider(DemoPlayer player, NavigationManager navigation, IStringLocalizer<SharedResource> localizer) : ICommandPaletteProvider
{
    public Task<IReadOnlyList<PaletteItem>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        string group = localizer["Palette.Group.Help"];
        List<PaletteItem> items =
        [
            new(group, localizer["Demo.Palette.Open"], () =>
            {
                navigation.NavigateTo("demo");
                return Task.CompletedTask;
            }),
        ];
        if (player.IsActive)
        {
            items.Add(new(group, localizer["Demo.Palette.Stop"], player.StopAsync));
        }

        return Task.FromResult<IReadOnlyList<PaletteItem>>([.. items.Where(i => PaletteMatcher.Score(i.Label, query) > 0)]);
    }
}
