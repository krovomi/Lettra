using Lettra.Contracts.Banking;

namespace Lettra.UI.Shared.Brief;

/// <summary>Entrées et sorties du relevé bancaire sur une période.</summary>
public sealed record BankFlows(decimal In, decimal Out)
{
    public decimal Net => In - Out;
}

/// <summary>
/// Les chiffres de « En bref », l'écran du dirigeant sur son téléphone, tirés de ce que les écrans du cabinet lisent
/// déjà : encaissements, relevé bancaire. Aucune règle de gestion n'est recalculée ici, seulement des sommes et des
/// sélections sur une période.
/// </summary>
public static class OwnerBrief
{
    /// <summary>Le montant à partir duquel un encaissement est signalé dans « À surveiller ».</summary>
    public const decimal DefaultThreshold = 1000m;

    /// <summary>Les encaissements signalés sont ceux des 30 derniers jours.</summary>
    public const int WatchDays = 30;

    /// <summary>Les encaissements se lisent en une page, la plus grande que le serveur accepte.</summary>
    public const int PaymentsPageSize = 200;

    public static IReadOnlyList<decimal> Thresholds { get; } = [500m, 1000m, 5000m, 10000m];

    public static DateOnly MonthStart(DateOnly today) => new(today.Year, today.Month, 1);

    /// <summary>Le début de la lecture des encaissements : le premier du mois, ou 30 jours plus tôt si c'est avant.</summary>
    public static DateOnly PaymentsFrom(DateOnly today) => DateOnlyMin(MonthStart(today), today.AddDays(-WatchDays));

    /// <summary>Encaissé entre deux dates, sans les encaissements contre-passés.</summary>
    public static decimal Collected(IEnumerable<PaymentSummary> payments, DateOnly from, DateOnly to) =>
        Posted(payments, from, to).Sum(p => p.Amount);

    /// <summary>Les encaissements d'au moins <paramref name="threshold"/> depuis 30 jours, les plus récents d'abord.</summary>
    public static IReadOnlyList<PaymentSummary> BigPayments(IEnumerable<PaymentSummary> payments, DateOnly today, decimal threshold) =>
        [.. Posted(payments, today.AddDays(-WatchDays), today).Where(p => p.Amount >= threshold).OrderByDescending(p => p.Date).ThenByDescending(p => p.Amount)];

    /// <summary>Entrées et sorties du relevé entre deux dates, opérations ignorées comprises : l'argent a bien bougé.</summary>
    public static BankFlows Flows(IEnumerable<BankLineResponse> lines, DateOnly from, DateOnly to)
    {
        BankLineResponse[] period = [.. lines.Where(l => l.BookingDate >= from && l.BookingDate <= to)];
        return new BankFlows(period.Where(l => l.Amount > 0m).Sum(l => l.Amount), -period.Where(l => l.Amount < 0m).Sum(l => l.Amount));
    }

    private static IEnumerable<PaymentSummary> Posted(IEnumerable<PaymentSummary> payments, DateOnly from, DateOnly to) =>
        payments.Where(p => p.Status == PaymentStatusDto.Posted && p.Date >= from && p.Date <= to);

    private static DateOnly DateOnlyMin(DateOnly a, DateOnly b) => a < b ? a : b;
}
