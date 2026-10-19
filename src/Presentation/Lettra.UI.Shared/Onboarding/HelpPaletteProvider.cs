using Lettra.UI.Shared.Components.Palette;
using Lettra.UI.Shared.Localization;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Onboarding;

/// <summary>
/// Palette Ctrl+K : « Aide et questions fréquentes », et dès trois lettres les questions de l'aide qui répondent à ce
/// qui est tapé (« avoir » propose « Comment corriger une facture déjà validée ? »), ouvertes dépliées dans l'aide.
/// </summary>
public sealed class HelpPaletteProvider(HelpPanelState help, IStringLocalizer<SharedResource> localizer) : ICommandPaletteProvider
{
    private const int MaxAnswers = 3;

    public Task<IReadOnlyList<PaletteItem>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        string group = localizer["Palette.Group.Help"];
        List<PaletteItem> items = [];
        string open = localizer["Help.Palette.Open"];
        if (PaletteMatcher.Score(open, query) > 0)
        {
            items.Add(new PaletteItem(group, open, () => Open(query: null, entryId: null)));
        }

        if (query is { Length: >= 3 })
        {
            items.AddRange(HelpCatalog.Search(query, key => localizer[key], everyWord: true).Take(MaxAnswers)
                .Select(entry => new PaletteItem(group, localizer[entry.QuestionKey], () => Open(query: null, entry.Id))));
        }

        return Task.FromResult<IReadOnlyList<PaletteItem>>(items);
    }

    private Task Open(string? query, string? entryId)
    {
        help.Open(query, entryId);
        return Task.CompletedTask;
    }
}
