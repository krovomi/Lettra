using System.Globalization;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Pilotage;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Pages.Pilotage;

/// <summary>
/// Le commentaire écrit sous chaque bloc des tableaux de bord : quelques phrases tirées des mêmes valeurs et des mêmes
/// seuils que les vignettes (<see cref="PilotageDisplay"/>), sans IA. Les mêmes chiffres donnent toujours le même texte,
/// que l'on peut copier dans un rapport au client. Ratios, délais et indices seulement : aucun montant, aucun nom.
/// </summary>
public static class PilotageCommentary
{
    /// <summary>En dessous de cette évolution (±2 %), le chiffre d'affaires est dit stable.</summary>
    public const double StableTrend = 0.02;

    /// <summary>Délai, retard, encaissement et dépendance au premier client.</summary>
    public static string Clients(DossierPilotageDto dossier, IStringLocalizer l, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(dossier);
        ArgumentNullException.ThrowIfNull(l);
        List<string> sentences = [];
        if (dossier.DaysSalesOutstanding is { } dso)
        {
            sentences.Add(Format(l, culture, $"Pilotage.Comment.Dso.{PilotageDisplay.Dso(dso)}", Format(l, culture, "Pilotage.Comment.Days", Math.Round(dso, MidpointRounding.AwayFromZero))));
        }

        if (dossier.OverdueReceivablesRatio is { } overdue)
        {
            sentences.Add(overdue == 0
                ? l["Pilotage.Comment.Overdue.None"]
                : Format(l, culture, $"Pilotage.Comment.Overdue.{PilotageDisplay.Overdue(overdue)}", PilotageDisplay.Percent(overdue, culture)));
        }

        if (dossier.CollectionRatio is { } collection)
        {
            sentences.Add(Format(l, culture, $"Pilotage.Comment.Collection.{PilotageDisplay.Collection(collection)}", PilotageDisplay.Percent(collection, culture)));
        }

        if (dossier.TopCustomerShareRatio is { } top)
        {
            sentences.Add(Format(l, culture, $"Pilotage.Comment.TopCustomer.{PilotageDisplay.Concentration(top)}", PilotageDisplay.Percent(top, culture), dossier.ActiveCustomers));
        }

        return Join(sentences, l);
    }

    /// <summary>Évolution du chiffre d'affaires et devis en attente.</summary>
    public static string Sales(DossierPilotageDto dossier, IStringLocalizer l, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(dossier);
        ArgumentNullException.ThrowIfNull(l);
        List<string> sentences = [];
        if (dossier.RevenueTrendRatio is { } trend)
        {
            string key = Math.Abs(trend) < StableTrend ? "Stable" : trend > 0 ? "Up" : "Down";
            sentences.Add(Format(l, culture, $"Pilotage.Comment.Trend.{key}", Math.Abs(trend).ToString("P0", culture)));
        }

        if (dossier.QuoteConversionRatio is { } conversion)
        {
            sentences.Add(Format(l, culture, "Pilotage.Comment.Conversion", PilotageDisplay.Percent(conversion, culture)));
        }

        sentences.Add(dossier switch
        {
            { QuotesPending: 0 } => l["Pilotage.Comment.Pending.None"],
            { QuotesExpiringSoon: > 0 } => Format(l, culture, Counted("Pilotage.Comment.Pending.Expiring", dossier.QuotesPending), dossier.QuotesPending, dossier.QuotesExpiringSoon),
            _ => Format(l, culture, Counted("Pilotage.Comment.Pending", dossier.QuotesPending), dossier.QuotesPending),
        });
        return Join(sentences, l);
    }

    /// <summary>
    /// L'activité du dernier mois complet comparée au mois moyen, et le meilleur mois de l'année. Le mois en cours n'est
    /// pas commenté : il n'est pas fini.
    /// </summary>
    public static string Activity(IReadOnlyList<MonthlyActivityDto> months, IStringLocalizer l, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(months);
        ArgumentNullException.ThrowIfNull(l);
        if (months.Count < 2 || months.All(m => m.RevenueIndex is null))
        {
            return l["Pilotage.Comment.Activity.None"];
        }

        MonthlyActivityDto last = months[^2];
        double index = last.RevenueIndex ?? 0;
        string gap = (Math.Abs(index - 100) / 100).ToString("P0", culture);
        string first = Math.Abs(index - 100) < StableTrend * 100
            ? Format(l, culture, "Pilotage.Comment.Activity.Average", Month(last, culture), index.ToString("N0", culture))
            : Format(l, culture, index > 100 ? "Pilotage.Comment.Activity.Above" : "Pilotage.Comment.Activity.Below", Month(last, culture), index.ToString("N0", culture), gap);
        MonthlyActivityDto best = months.Take(months.Count - 1).OrderByDescending(m => m.RevenueIndex ?? 0).ThenByDescending(m => m.Month, StringComparer.Ordinal).First();
        string second = best == last
            ? l["Pilotage.Comment.Activity.BestIsLast"]
            : Format(l, culture, "Pilotage.Comment.Activity.Best", Month(best, culture), (best.RevenueIndex ?? 0).ToString("N0", culture));
        return Join([first, second], l);
    }

    /// <summary>Les factures du dernier mois complet comparées au mois d'avant, et le total sur douze mois.</summary>
    public static string Invoices(IReadOnlyList<MonthlyActivityDto> months, IStringLocalizer l, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(months);
        ArgumentNullException.ThrowIfNull(l);
        int total = months.Sum(m => m.InvoicesIssued);
        if (months.Count < 3 || total == 0)
        {
            return l["Pilotage.Comment.Invoices.None"];
        }

        MonthlyActivityDto last = months[^2];
        MonthlyActivityDto before = months[^3];
        string first = Format(l, culture, Counted("Pilotage.Comment.Invoices", last.InvoicesIssued), last.InvoicesIssued, Month(last, culture), before.InvoicesIssued, Month(before, culture));
        return Join([first, Format(l, culture, "Pilotage.Comment.Invoices.Total", total)], l);
    }

    /// <summary>L'onglet « Mes dossiers » : combien de dossiers sont à surveiller, et le délai moyen.</summary>
    public static string Cabinet(IReadOnlyList<DossierPilotageDto> dossiers, IStringLocalizer l, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(dossiers);
        ArgumentNullException.ThrowIfNull(l);
        int late = dossiers.Count(d => PilotageDisplay.Overdue(d.OverdueReceivablesRatio) >= IndicatorLevelDto.Warning);
        int slow = dossiers.Count(d => PilotageDisplay.Dso(d.DaysSalesOutstanding) >= IndicatorLevelDto.Warning);
        int falling = dossiers.Count(d => PilotageDisplay.Trend(d.RevenueTrendRatio) >= IndicatorLevelDto.Warning);
        List<string> sentences = [Format(l, culture, Counted("Pilotage.Comment.Cabinet.Count", dossiers.Count), dossiers.Count)];
        if (late + slow + falling == 0)
        {
            sentences.Add(l["Pilotage.Comment.Cabinet.AllGood"]);
        }

        foreach ((string key, int count) in new[] { ("Late", late), ("Slow", slow), ("Falling", falling) }.Where(c => c.Item2 > 0))
        {
            sentences.Add(Format(l, culture, Counted($"Pilotage.Comment.Cabinet.{key}", count), count));
        }

        return Join(sentences, l);
    }

    /// <summary>
    /// La comparaison aux pairs : combien de dossiers, puis les indicateurs mieux, moins bien et proches de la médiane.
    /// Vide tant que la comparaison n'est pas disponible (l'écran dit pourquoi).
    /// </summary>
    public static string Peers(PeerComparisonResponse peers, IStringLocalizer l, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(peers);
        ArgumentNullException.ThrowIfNull(l);
        if (peers.Status != PeerComparisonStatusDto.Available)
        {
            return string.Empty;
        }

        List<string> sentences = [Format(l, culture, "Pilotage.Comment.Peers.Intro", peers.Peers, peers.Sector)];
        PeerMetricComparisonDto[] placed = [.. peers.Metrics.Where(m => m.Position is not null)];
        if (placed.Length > 0 && placed.All(m => m.Position == PeerPositionDto.Similar))
        {
            sentences.Add(l["Pilotage.Comment.Peers.AllSimilar"]);
            return Join(sentences, l);
        }

        foreach (PeerPositionDto position in new[] { PeerPositionDto.Better, PeerPositionDto.Worse, PeerPositionDto.Similar })
        {
            string[] names = [.. placed.Where(m => m.Position == position).Select(m => l[$"Pilotage.Peers.Name.{m.Metric}"].Value)];
            if (names.Length > 0)
            {
                sentences.Add(Format(l, culture, $"Pilotage.Comment.Peers.{position}", List(names, l)));
            }
        }

        return Join(sentences, l);
    }

    /// <summary>« a, b et c ».</summary>
    private static string List(string[] names, IStringLocalizer l) =>
        names.Length == 1 ? names[0] : string.Join(", ", names[..^1]) + l["Pilotage.Comment.Peers.And"] + names[^1];

    /// <summary>La phrase au singulier pour un seul élément, au pluriel au-delà.</summary>
    private static string Counted(string key, int count) => key + (count == 1 ? ".One" : ".Many");

    private static string Month(MonthlyActivityDto month, CultureInfo culture) => PilotageDisplay.Month(month.Month, true, culture);

    private static string Format(IStringLocalizer l, CultureInfo culture, string key, params object?[] values) =>
        string.Format(culture, l[key], values);

    private static string Join(List<string> sentences, IStringLocalizer l) =>
        sentences.Count == 0 ? l["Pilotage.Comment.Empty"] : string.Join(' ', sentences);
}
