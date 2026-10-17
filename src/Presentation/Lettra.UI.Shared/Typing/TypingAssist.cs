using System.Globalization;
using Lettra.UI.Shared.Components.Palette;
using Lettra.UI.Shared.Localization;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Typing;

/// <summary>Une proposition d'un champ assisté : la valeur à poser, son libellé, un complément et l'objet d'origine.</summary>
public sealed record Suggestion(string Value, string Label, string? Hint = null, object? Data = null);

/// <summary>Champ qui accepte des modèles de texte (pour la palette Ctrl+K).</summary>
public interface ITemplateTarget
{
    TextFieldKind Kind { get; }

    Task InsertAsync(string text);
}

/// <summary>
/// Dernier champ de texte libre où l'utilisateur est passé : la palette, qui prend le focus en s'ouvrant, y insère le
/// modèle choisi.
/// </summary>
public sealed class TypingAssist
{
    public ITemplateTarget? ActiveField { get; private set; }

    public void Focused(ITemplateTarget field) => ActiveField = field;

    public void Released(ITemplateTarget field)
    {
        if (ReferenceEquals(ActiveField, field))
        {
            ActiveField = null;
        }
    }
}

/// <summary>Palette Ctrl+K : « Insérer : … » les modèles du dernier champ de texte visité.</summary>
public sealed class TemplatePaletteProvider(TypingAssist assist, IStringLocalizer<SharedResource> localizer) : ICommandPaletteProvider
{
    public Task<IReadOnlyList<PaletteItem>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (assist.ActiveField is not { Kind: not TextFieldKind.Free } field)
        {
            return Task.FromResult<IReadOnlyList<PaletteItem>>([]);
        }

        string group = localizer["Palette.Group.Templates"];
        IReadOnlyList<PaletteItem> items =
        [
            .. TextTemplates.For(field.Kind)
                .Select(t => new PaletteItem(group,
                    string.Format(CultureInfo.CurrentCulture, localizer["Template.Palette.Insert"], localizer[t.TitleKey]),
                    () => field.InsertAsync(localizer[t.BodyKey])))
                .Where(i => PaletteMatcher.Score(i.Label, query) > 0),
        ];
        return Task.FromResult(items);
    }
}
