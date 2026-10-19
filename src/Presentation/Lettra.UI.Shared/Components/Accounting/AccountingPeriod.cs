using System.Globalization;
using Lettra.Calculations;

namespace Lettra.UI.Shared.Components.Accounting;

/// <summary>Raccourcis de période des pages Comptabilité.</summary>
public enum PeriodPreset
{
    ThisMonth,
    LastMonth,
    ThisQuarter,
    ThisYear,
    LastYear,
}

/// <summary>
/// Période consultée (bornes comprises), portée par l'adresse (<c>?from=2026-01-01&amp;to=2026-12-31</c>) pour que les
/// liens entre journal, balance et grand livre la gardent. Sans période : l'année civile en cours.
/// </summary>
public readonly record struct AccountingPeriod(DateOnly From, DateOnly To)
{
    public static AccountingPeriod Of(PeriodPreset preset, DateOnly today)
    {
        DateOnly month = new(today.Year, today.Month, 1);
        DateOnly quarter = new(today.Year, ((today.Month - 1) / 3 * 3) + 1, 1);
        return preset switch
        {
            PeriodPreset.ThisMonth => new(month, month.AddMonths(1).AddDays(-1)),
            PeriodPreset.LastMonth => new(month.AddMonths(-1), month.AddDays(-1)),
            PeriodPreset.ThisQuarter => new(quarter, quarter.AddMonths(3).AddDays(-1)),
            PeriodPreset.LastYear => new(new DateOnly(today.Year - 1, 1, 1), new DateOnly(today.Year - 1, 12, 31)),
            _ => new(new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31)),
        };
    }

    /// <summary>Période lue dans l'adresse ; une borne absente ou illisible prend celle de l'année en cours.</summary>
    public static AccountingPeriod Parse(string? from, string? to, DateOnly today)
    {
        AccountingPeriod year = Of(PeriodPreset.ThisYear, today);
        return new(ParseDay(from) ?? year.From, ParseDay(to) ?? year.To);
    }

    public PeriodPreset? Preset(DateOnly today)
    {
        foreach (PeriodPreset preset in Enum.GetValues<PeriodPreset>())
        {
            if (Of(preset, today) == this)
            {
                return preset;
            }
        }

        return null;
    }

    public string Query => $"from={Day(From)}&to={Day(To)}";

    public static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly? ParseDay(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day) ? day : null;
}

/// <summary>Intitulés des comptes du plan comptable utilisés par les écritures de vente et de banque (clés <c>Account.&lt;numéro&gt;</c>).</summary>
public static class AccountNames
{
    /// <summary>Comptes que Lettra mouvemente aujourd'hui, dans l'ordre du plan comptable.</summary>
    public static IReadOnlyList<string> Known { get; } =
    [
        SalesAccounts.DownPaymentsReceived,
        SalesAccounts.Customers,
        SalesAccounts.VatCollected,
        SalesAccounts.VatPending,
        PaymentAccounts.Bank,
        PaymentAccounts.WriteOffLoss,
        SalesAccounts.ServicesRevenue,
        SalesAccounts.GoodsRevenue,
        PaymentAccounts.WriteOffGain,
    ];

    /// <summary>Chiffre d'affaires : les comptes 70 (ventes), pas les produits divers (758) des écarts de règlement.</summary>
    public static bool IsRevenue(string account) => account.StartsWith("70", StringComparison.Ordinal);
}
