using System.Globalization;
using System.Text.RegularExpressions;
using Lettra.Contracts.Decisions;
using Lettra.UI.Shared.Components.Dates;
using Lettra.UI.Shared.Components.Explorer;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Pages.Decisions;

/// <summary>
/// Les phrases d'une décision, dans la langue de l'écran : <c>Decision.&lt;Kind&gt;.Title</c> et <c>.Reason</c>, dont les
/// emplacements nommés (<c>{customer}</c>) prennent les faits envoyés par la règle. Une valeur qui commence par <c>@</c>
/// est une clé de libellé, une date <c>aaaa-mm-jj</c> s'écrit dans la langue de l'écran, un fait dont le nom finit par
/// <c>Amount</c> est un montant en euros. Aucune phrase n'est écrite par une IA.
/// </summary>
public static partial class DecisionText
{
    public const string LabelPrefix = "@";
    public const string AmountSuffix = "Amount";

    public static string Title(IStringLocalizer localizer, DecisionRow row, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(row);
        return Phrase(localizer, $"Decision.{row.Kind}.Title", row, culture) ?? string.Format(culture, localizer["Decisions.Unknown"], row.Kind);
    }

    public static string? Reason(IStringLocalizer localizer, DecisionRow row, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Phrase(localizer, $"Decision.{row.Kind}.Reason", row, culture);
    }

    /// <summary>Le libellé du bouton « Accepter » : ce que l'action fait (« Envoyer la relance »), sinon « Accepter ».</summary>
    public static string AcceptLabel(IStringLocalizer localizer, DecisionRow row)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(row);
        LocalizedString label = localizer[$"Decision.{row.Kind}.Accept"];
        return label.ResourceNotFound ? localizer["Decisions.Accept"] : label.Value;
    }

    public static string Fill(IStringLocalizer localizer, string template, IReadOnlyDictionary<string, string> facts, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(facts);
        return Placeholder().Replace(template, m => facts.TryGetValue(m.Groups[1].Value, out string? value) ? Value(localizer, m.Groups[1].Value, value, culture) : m.Value);
    }

    public static string Value(IStringLocalizer localizer, string name, string value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        if (value.StartsWith(LabelPrefix, StringComparison.Ordinal))
        {
            LocalizedString label = localizer[value[LabelPrefix.Length..]];
            return label.ResourceNotFound ? value[LabelPrefix.Length..] : label.Value;
        }

        if (DateText.ParseIso(value) is { } date)
        {
            return DateText.Format(date, culture);
        }

        return name.EndsWith(AmountSuffix, StringComparison.Ordinal) && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount)
            ? ExplorerText.Money(amount)
            : value;
    }

    private static string? Phrase(IStringLocalizer localizer, string key, DecisionRow row, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        LocalizedString template = localizer[key];
        return template.ResourceNotFound ? null : Fill(localizer, template.Value, row.Facts, culture);
    }

    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9]*)\}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Placeholder();
}
