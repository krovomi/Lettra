using Lettra.UI.Shared.Components.Palette;
using Lettra.UI.Shared.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Onboarding;

/// <summary>Palette Ctrl+K : « Tutoriel de cet écran » et « Réinitialiser les tutoriels ».</summary>
public sealed class TourPaletteProvider(GuidedTours tours, NavigationManager navigation, IStringLocalizer<SharedResource> localizer) : ICommandPaletteProvider
{
    public Task<IReadOnlyList<PaletteItem>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        string group = localizer["Palette.Group.Help"];
        List<PaletteItem> items =
        [
            new(group, localizer["Tour.Palette.Replay"], () =>
            {
                if ((TourCatalog.ForPath(navigation.ToBaseRelativePath(navigation.Uri)) ?? TourCatalog.Find("home")) is { } tour)
                {
                    tours.Start(tour);
                }

                return Task.CompletedTask;
            }),
            new(group, localizer["Tour.Palette.Reset"], tours.ResetAsync),
        ];
        return Task.FromResult<IReadOnlyList<PaletteItem>>([.. items.Where(i => PaletteMatcher.Score(i.Label, query) > 0)]);
    }
}
