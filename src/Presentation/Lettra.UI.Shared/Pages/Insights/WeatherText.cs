using System.Globalization;
using Lettra.Contracts.Insights;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Pages.Insights;

/// <summary>L'icône et les mots de la météo des paiements, partagés par la météo et la fiche d'appel.</summary>
internal static class WeatherText
{
    public static string Icon(PaymentWeatherDto weather) => weather switch
    {
        PaymentWeatherDto.Sunny => "sun",
        PaymentWeatherDto.Cloudy => "cloud-sun",
        PaymentWeatherDto.Rainy => "cloud-rain",
        PaymentWeatherDto.Stormy => "cloud-lightning",
        _ => "cloud",
    };

    /// <summary>Un retard en jours : « 3 j d'avance », « à l'échéance », « 12 j après l'échéance ».</summary>
    public static string Days(IStringLocalizer l, int days)
    {
        ArgumentNullException.ThrowIfNull(l);
        return days switch
        {
            < 0 => string.Format(CultureInfo.CurrentCulture, l["Weather.Days.Early"], -days),
            0 => l["Weather.Days.OnTime"],
            _ => string.Format(CultureInfo.CurrentCulture, l["Weather.Days.Late"], days),
        };
    }
}
