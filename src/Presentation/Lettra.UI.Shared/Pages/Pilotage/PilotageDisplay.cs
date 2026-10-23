using System.Globalization;
using Lettra.Contracts.Cabinet;

namespace Lettra.UI.Shared.Pages.Pilotage;

/// <summary>
/// Affichage des indicateurs de pilotage : formats et seuils de couleur, les mêmes que les tableaux Grafana
/// (docs/indicateurs-grafana.md). Vert : tout va bien ; orange : à surveiller ; rouge : à traiter.
/// </summary>
public static class PilotageDisplay
{
    /// <summary>Délai moyen de paiement des clients : orange à 45 jours, rouge à 60.</summary>
    public static IndicatorLevelDto Dso(double? days) => Above(days, 45, 60);

    /// <summary>Part des créances en retard : orange à 20 %, rouge à 40 %.</summary>
    public static IndicatorLevelDto Overdue(double? ratio) => Above(ratio, 0.2, 0.4);

    /// <summary>Part encaissée sur 12 mois : orange sous 80 %, rouge sous 60 %.</summary>
    public static IndicatorLevelDto Collection(double? ratio) => ratio switch
    {
        null => IndicatorLevelDto.Ok,
        < 0.6 => IndicatorLevelDto.Critical,
        < 0.8 => IndicatorLevelDto.Warning,
        _ => IndicatorLevelDto.Ok,
    };

    /// <summary>Poids du premier client : orange à 30 %, rouge à 50 % (dépendance à un client).</summary>
    public static IndicatorLevelDto Concentration(double? ratio) => Above(ratio, 0.3, 0.5);

    /// <summary>Évolution du chiffre d'affaires : orange sous −10 %, rouge sous −25 %.</summary>
    public static IndicatorLevelDto Trend(double? ratio) => ratio switch
    {
        null => IndicatorLevelDto.Ok,
        < -0.25 => IndicatorLevelDto.Critical,
        < -0.1 => IndicatorLevelDto.Warning,
        _ => IndicatorLevelDto.Ok,
    };

    /// <summary>Âge du calcul quotidien de la vue cabinet : orange au-delà de 26 h, rouge au-delà de 50 h (un passage manqué).</summary>
    public static IndicatorLevelDto NightlyAge(double? seconds) => Above(seconds, 26 * 3600, 50 * 3600);

    /// <summary>Un poste silencieux (plus de 7 jours sans synchronisation) est à vérifier.</summary>
    public static IndicatorLevelDto Silent(int count) => count > 0 ? IndicatorLevelDto.Warning : IndicatorLevelDto.Ok;

    public static string Days(double? days, CultureInfo culture) =>
        days is double value ? string.Create(culture, $"{Math.Round(value, MidpointRounding.AwayFromZero):N0} j") : "—";

    public static string Percent(double? ratio, CultureInfo culture) =>
        ratio is double value ? value.ToString("P0", culture) : "—";

    /// <summary>Une évolution, toujours signée : « +12 % », « −8 % ».</summary>
    public static string SignedPercent(double? ratio, CultureInfo culture)
    {
        if (ratio is not double value)
        {
            return "—";
        }

        string magnitude = Math.Abs(value).ToString("P0", culture);
        return Math.Round(value, 2, MidpointRounding.AwayFromZero) switch
        {
            > 0 => "+" + magnitude,
            < 0 => "−" + magnitude,
            _ => magnitude,
        };
    }

    /// <summary>Une durée lisible : « 12 min », « 5 h », « 3 j ».</summary>
    public static string Duration(double? seconds, CultureInfo culture)
    {
        if (seconds is not double value)
        {
            return "—";
        }

        TimeSpan span = TimeSpan.FromSeconds(Math.Max(0, value));
        return span switch
        {
            { TotalMinutes: < 1 } => string.Create(culture, $"{span.TotalSeconds:N0} s"),
            { TotalHours: < 1 } => string.Create(culture, $"{Math.Floor(span.TotalMinutes):N0} min"),
            { TotalDays: < 2 } => string.Create(culture, $"{Math.Floor(span.TotalHours):N0} h"),
            _ => string.Create(culture, $"{Math.Floor(span.TotalDays):N0} j"),
        };
    }

    /// <summary>Une taille en octets, en unités binaires : « 1,5 Mio ».</summary>
    public static string Bytes(long? bytes, CultureInfo culture)
    {
        if (bytes is not long value)
        {
            return "—";
        }

        string[] units = ["o", "Kio", "Mio", "Gio", "Tio"];
        double size = value;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return string.Create(culture, $"{size.ToString(unit == 0 ? "N0" : "N1", culture)} {units[unit]}");
    }

    /// <summary>Hauteur d'une barre en pourcentage du plus grand (au moins un trait pour une valeur non nulle).</summary>
    public static string BarHeight(double value, double max) =>
        (max <= 0 ? 0 : Math.Clamp(value / max * 100, value > 0 ? 2 : 0, 100)).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    /// <summary>Le mois <c>2026-10</c> en « oct. » (court) ou « octobre 2026 » (long).</summary>
    public static string Month(string month, bool full, CultureInfo culture) =>
        DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            ? date.ToString(full ? "MMMM yyyy" : "MMM", culture)
            : month;

    private static IndicatorLevelDto Above(double? value, double warning, double critical) => value switch
    {
        null => IndicatorLevelDto.Ok,
        _ when value >= critical => IndicatorLevelDto.Critical,
        _ when value >= warning => IndicatorLevelDto.Warning,
        _ => IndicatorLevelDto.Ok,
    };
}
