using System.Globalization;
using Lettra.Contracts.Insights;
using Lettra.UI.Shared.Components.Explorer;
using Lettra.UI.Shared.Localization;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Components.Prevention;

/// <summary>
/// Les phrases du radar de prévention, écrites à l'écran à partir des chiffres rendus par le serveur : une phrase par
/// signal et par état (allumé, rien à signaler), la raison d'un signal non évalué, et la mise en forme de chaque chiffre
/// dans la langue de l'écran. Les chiffres arrivent dans un ordre fixe par signal ; la phrase les cite par position.
/// </summary>
public static class PreventionText
{
    /// <summary>Les signaux dans l'ordre de l'écran (celui du serveur).</summary>
    public static IReadOnlyList<PreventionSignalDto> Signals { get; } = Enum.GetValues<PreventionSignalDto>();

    public static string Level(IStringLocalizer<SharedResource> localizer, PreventionLevelDto level)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return localizer["Prevention.Level." + level];
    }

    public static string Title(IStringLocalizer<SharedResource> localizer, PreventionSignalDto signal)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return localizer["Prevention.Signal." + signal];
    }

    public static string State(IStringLocalizer<SharedResource> localizer, PreventionSignalStateDto state)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return localizer["Prevention.State." + state];
    }

    /// <summary>Le nom de la classe de couleur d'un état : calme, à surveiller, grave, gris pour le reste.</summary>
    public static string Tone(PreventionSignalStateDto state) => state switch
    {
        PreventionSignalStateDto.Call => "Call",
        PreventionSignalStateDto.Watch => "Watch",
        PreventionSignalStateDto.Clear => "Calm",
        _ => "Muted",
    };

    public static string Icon(PreventionSignalDto signal) => signal switch
    {
        PreventionSignalDto.Treasury => "wallet",
        PreventionSignalDto.PaymentDelay => "calendar-clock",
        PreventionSignalDto.PeerGap => "trending-down",
        PreventionSignalDto.VatBeyondCollections => "landmark",
        PreventionSignalDto.TopCustomer => "user",
        PreventionSignalDto.CustomersInProceedings => "gavel",
        PreventionSignalDto.BilledCollectedGap => "hand-coins",
        _ => "radar",
    };

    /// <summary>
    /// La phrase d'un signal : les chiffres et la raison s'il est allumé ou calme, ce qui manque s'il n'est pas évalué,
    /// rien s'il est désactivé dans les seuils du dossier.
    /// </summary>
    public static string Sentence(IStringLocalizer<SharedResource> localizer, PreventionSignalResponse signal, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(culture);
        switch (signal.State)
        {
            case PreventionSignalStateDto.Disabled:
                return localizer["Prevention.Disabled"];
            case PreventionSignalStateDto.NotEvaluated:
                return string.Format(culture, localizer["Prevention.Unavailable." + signal.Unavailable], Figures(localizer, signal, culture));
            default:
                string key = $"Prevention.Signal.{signal.Signal}.{(signal.IsActive ? "Active" : "Clear")}";
                return string.Format(culture, localizer[key], Figures(localizer, signal, culture));
        }
    }

    /// <summary>Le libellé de l'effet chiffré du signal (le manque de trésorerie, la créance exposée…).</summary>
    public static string Impact(IStringLocalizer<SharedResource> localizer, PreventionSignalDto signal)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return localizer["Prevention.Impact." + signal];
    }

    /// <summary>L'échéance d'un signal (« sous zéro à partir du », « à payer le »…), générique si le signal n'en a pas.</summary>
    public static string DueOn(IStringLocalizer<SharedResource> localizer, PreventionSignalDto signal, DateOnly due, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        LocalizedString specific = localizer["Prevention.DueOn." + signal];
        string format = specific.ResourceNotFound ? localizer["Prevention.DueOn"] : specific.Value;
        return string.Format(culture, format, LongDate(due, culture));
    }

    public static string Money(decimal value) => ExplorerText.Money(value);

    /// <summary>« 3 novembre 2026 » : les phrases lisent mieux une date en toutes lettres.</summary>
    public static string LongDate(DateOnly date, CultureInfo culture) => date.ToString("d MMMM yyyy", culture);

    /// <summary>
    /// Une évolution, toujours signée, au dixième (« +4,0 % », « −12,5 % ») : le signe et le symbole suivent la langue de
    /// l'écran, comme dans les tableaux de bord.
    /// </summary>
    public static string SignedPercent(decimal value, CultureInfo culture)
    {
        string magnitude = (Math.Abs(value) / 100m).ToString("P1", culture);
        return Math.Round(value, 1, MidpointRounding.AwayFromZero) switch
        {
            > 0 => "+" + magnitude,
            < 0 => "−" + magnitude,
            _ => magnitude,
        };
    }

    /// <summary>Une part ou un seuil (12,5 pour 12,5 %) : sans décimale s'il est entier, au dixième sinon.</summary>
    public static string Percent(decimal value, CultureInfo culture) =>
        (value / 100m).ToString(decimal.IsInteger(Math.Round(value, 1, MidpointRounding.AwayFromZero)) ? "P0" : "P1", culture);

    public static string Days(IStringLocalizer<SharedResource> localizer, decimal value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return string.Format(culture, localizer["Prevention.Days"], value.ToString("N0", culture));
    }

    private static object[] Figures(IStringLocalizer<SharedResource> localizer, PreventionSignalResponse signal, CultureInfo culture) =>
        [.. signal.Figures.Select(f => (object)Figure(localizer, signal.Signal, f, culture))];

    private static string Figure(IStringLocalizer<SharedResource> localizer, PreventionSignalDto signal, PreventionFigureDto figure, CultureInfo culture)
    {
        decimal number = figure.Number ?? 0m;
        return figure.Kind switch
        {
            PreventionFigureKindDto.Amount => Money(number),
            PreventionFigureKindDto.Days => Days(localizer, number, culture),
            // L'évolution du chiffre d'affaires et sa médiane sont signées ; une part ou un seuil ne l'est pas.
            PreventionFigureKindDto.Percent when signal == PreventionSignalDto.PeerGap => SignedPercent(number, culture),
            PreventionFigureKindDto.Percent => Percent(number, culture),
            PreventionFigureKindDto.Count => number.ToString("N0", culture),
            PreventionFigureKindDto.Date when figure.Date is DateOnly date => LongDate(date, culture),
            // L'échéance de TVA porte le nom de l'obligation, traduit comme dans l'écran Déclarations.
            PreventionFigureKindDto.Text when figure.Name == "obligation" => Obligation(localizer, figure.Text),
            _ => figure.Text ?? string.Empty,
        };
    }

    private static string Obligation(IStringLocalizer<SharedResource> localizer, string? obligation)
    {
        LocalizedString text = localizer["Tax.Obligation." + obligation];
        return text.ResourceNotFound ? obligation ?? string.Empty : text.Value;
    }
}
