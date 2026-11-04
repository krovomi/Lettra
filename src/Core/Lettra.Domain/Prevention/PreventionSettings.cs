using System.Globalization;
using Lettra.Domain.Common;

namespace Lettra.Domain.Prevention;

/// <summary>Les signaux du radar de prévention (valeurs identiques à <c>PreventionSignalDto</c>).</summary>
public enum PreventionSignal
{
    Treasury = 1,
    PaymentDelay = 2,
    PeerGap = 3,
    VatBeyondCollections = 4,
    TopCustomer = 5,
    CustomersInProceedings = 6,
    BilledCollectedGap = 7,
}

/// <summary>
/// Les seuils du radar de prévention d'un dossier (une seule ligne par base de dossier, créée au premier réglage).
/// Le cabinet les règle selon ce qu'il sait du client : un solde de trésorerie connu, des seuils plus ou moins
/// sensibles, des signaux coupés (une holding à client unique, par exemple). Tant qu'il n'a rien réglé, les valeurs
/// proposées par Lettra s'appliquent ; elles sont à relire avec un expert-comptable.
/// </summary>
public sealed class PreventionSettings : Entity<Guid>, IVersioned
{
    public static readonly Guid SingletonId = new("6c657474-7261-0000-0000-707265766e74");

    public const int DefaultHorizonWeeks = 8;
    public const int MinHorizonWeeks = 2;
    public const int MaxHorizonWeeks = 26;
    public const decimal MaxCashBalance = 999_999_999m;
    public const int DefaultPaymentDelayRiseDays = 5;
    public const int MinPaymentDelayRiseDays = 1;
    public const int MaxPaymentDelayRiseDays = 60;
    public const int DefaultPeerGapPoints = 15;
    public const int MinPeerGapPoints = 5;
    public const int MaxPeerGapPoints = 100;
    public const int DefaultTopCustomerSharePercent = 50;
    public const int MinTopCustomerSharePercent = 20;
    public const int MaxTopCustomerSharePercent = 100;
    public const int DefaultBilledCollectedGapPercent = 20;
    public const int MinBilledCollectedGapPercent = 5;
    public const int MaxBilledCollectedGapPercent = 100;
    public const int DefaultCallSignalCount = 3;
    public const int MinCallSignalCount = 2;
    public const int MaxCallSignalCount = 7;

    /// <summary>Longueur de la colonne des signaux coupés (« 1,5,7 »).</summary>
    public const int DisabledSignalsLength = 40;

    private PreventionSettings(Guid id)
        : base(id)
    {
    }

    /// <summary>Semaines de trésorerie prévue regardées.</summary>
    public int TreasuryHorizonWeeks { get; private set; }

    /// <summary>Le solde de trésorerie connu, indiqué par le cabinet ; <c>null</c> tant qu'il n'est pas connu.</summary>
    public decimal? CashBalance { get; private set; }

    /// <summary>La date de ce solde.</summary>
    public DateOnly? CashBalanceOn { get; private set; }

    /// <summary>Hausse du délai de paiement, en jours sur trois mois, à partir de laquelle le signal s'allume.</summary>
    public int PaymentDelayRiseDays { get; private set; }

    /// <summary>Retard, en points d'évolution du chiffre d'affaires, sur la médiane des pairs.</summary>
    public int PeerGapPoints { get; private set; }

    /// <summary>Part du premier client dans le chiffre d'affaires des douze derniers mois, en pourcentage.</summary>
    public int TopCustomerSharePercent { get; private set; }

    /// <summary>Part du facturé des trois derniers mois restée non encaissée, en pourcentage.</summary>
    public int BilledCollectedGapPercent { get; private set; }

    /// <summary>Nombre de signaux allumés à la fois qui fait passer le dossier « à appeler ».</summary>
    public int CallSignalCount { get; private set; }

    /// <summary>Les signaux coupés, rangés en texte (« 1,5 ») : une colonne, pas une table.</summary>
    public string DisabledSignals { get; private set; } = string.Empty;

    public long Version { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    /// <summary>Les signaux coupés pour ce dossier.</summary>
    public IReadOnlySet<PreventionSignal> Disabled =>
        DisabledSignals
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => (PreventionSignal)int.Parse(s, CultureInfo.InvariantCulture))
            .ToHashSet();

    public static PreventionSettings Default() => new(SingletonId)
    {
        TreasuryHorizonWeeks = DefaultHorizonWeeks,
        PaymentDelayRiseDays = DefaultPaymentDelayRiseDays,
        PeerGapPoints = DefaultPeerGapPoints,
        TopCustomerSharePercent = DefaultTopCustomerSharePercent,
        BilledCollectedGapPercent = DefaultBilledCollectedGapPercent,
        CallSignalCount = DefaultCallSignalCount,
        Version = 0,
    };

    /// <summary>Remplace tous les seuils ; refuse une valeur hors bornes sans rien changer.</summary>
    public Result Update(PreventionThresholds thresholds, Guid userId, DateTimeOffset now, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        List<Error> errors = [.. Validate(thresholds, today)];
        if (errors.Count > 0)
        {
            return Result.Failure(errors);
        }

        TreasuryHorizonWeeks = thresholds.TreasuryHorizonWeeks;
        CashBalance = thresholds.CashBalance is decimal balance ? decimal.Round(balance, 2, MidpointRounding.AwayFromZero) : null;
        CashBalanceOn = thresholds.CashBalance is null ? null : thresholds.CashBalanceOn;
        PaymentDelayRiseDays = thresholds.PaymentDelayRiseDays;
        PeerGapPoints = thresholds.PeerGapPoints;
        TopCustomerSharePercent = thresholds.TopCustomerSharePercent;
        BilledCollectedGapPercent = thresholds.BilledCollectedGapPercent;
        CallSignalCount = thresholds.CallSignalCount;
        DisabledSignals = string.Join(',', thresholds.Disabled.Distinct().Order().Select(s => ((int)s).ToString(CultureInfo.InvariantCulture)));
        UpdatedBy = userId;
        UpdatedAt = now;
        Version++;
        return Result.Success();
    }

    private static IEnumerable<Error> Validate(PreventionThresholds t, DateOnly today)
    {
        if (t.TreasuryHorizonWeeks is < MinHorizonWeeks or > MaxHorizonWeeks)
        {
            yield return PreventionErrors.HorizonInvalid;
        }

        if (t.CashBalance is decimal balance && Math.Abs(balance) > MaxCashBalance)
        {
            yield return PreventionErrors.CashBalanceInvalid;
        }

        if (t.CashBalance is not null && (t.CashBalanceOn is not DateOnly on || on > today))
        {
            yield return PreventionErrors.CashBalanceDateInvalid;
        }

        if (t.PaymentDelayRiseDays is < MinPaymentDelayRiseDays or > MaxPaymentDelayRiseDays)
        {
            yield return PreventionErrors.ThresholdInvalid(nameof(t.PaymentDelayRiseDays));
        }

        if (t.PeerGapPoints is < MinPeerGapPoints or > MaxPeerGapPoints)
        {
            yield return PreventionErrors.ThresholdInvalid(nameof(t.PeerGapPoints));
        }

        if (t.TopCustomerSharePercent is < MinTopCustomerSharePercent or > MaxTopCustomerSharePercent)
        {
            yield return PreventionErrors.ThresholdInvalid(nameof(t.TopCustomerSharePercent));
        }

        if (t.BilledCollectedGapPercent is < MinBilledCollectedGapPercent or > MaxBilledCollectedGapPercent)
        {
            yield return PreventionErrors.ThresholdInvalid(nameof(t.BilledCollectedGapPercent));
        }

        if (t.CallSignalCount is < MinCallSignalCount or > MaxCallSignalCount)
        {
            yield return PreventionErrors.ThresholdInvalid(nameof(t.CallSignalCount));
        }

        if (t.Disabled.Any(s => !Enum.IsDefined(s)))
        {
            yield return PreventionErrors.SignalUnknown;
        }
    }
}

/// <summary>Les seuils demandés par le cabinet, avant contrôle.</summary>
public sealed record PreventionThresholds(
    int TreasuryHorizonWeeks,
    decimal? CashBalance,
    DateOnly? CashBalanceOn,
    int PaymentDelayRiseDays,
    int PeerGapPoints,
    int TopCustomerSharePercent,
    int BilledCollectedGapPercent,
    int CallSignalCount,
    IReadOnlyCollection<PreventionSignal> Disabled);

/// <summary>Codes d'erreur du radar de prévention (mêmes valeurs que <c>ErrorCodes.Prevention</c>).</summary>
public static class PreventionErrors
{
    public const string HorizonInvalidCode = "Prevention.HorizonInvalid";
    public const string CashBalanceInvalidCode = "Prevention.CashBalanceInvalid";
    public const string CashBalanceDateInvalidCode = "Prevention.CashBalanceDateInvalid";
    public const string ThresholdInvalidCode = "Prevention.ThresholdInvalid";
    public const string SignalUnknownCode = "Prevention.SignalUnknown";

    public static Error HorizonInvalid => Error.Validation(HorizonInvalidCode, nameof(PreventionSettings.TreasuryHorizonWeeks));

    public static Error CashBalanceInvalid => Error.Validation(CashBalanceInvalidCode, nameof(PreventionSettings.CashBalance));

    public static Error CashBalanceDateInvalid => Error.Validation(CashBalanceDateInvalidCode, nameof(PreventionSettings.CashBalanceOn));

    public static Error SignalUnknown => Error.Validation(SignalUnknownCode, nameof(PreventionSettings.Disabled));

    public static Error ThresholdInvalid(string field) => Error.Validation(ThresholdInvalidCode, field);
}
