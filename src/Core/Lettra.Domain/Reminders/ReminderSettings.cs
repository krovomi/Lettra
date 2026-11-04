using Lettra.Domain.Common;

namespace Lettra.Domain.Reminders;

/// <summary>
/// Niveaux du scénario de relance, du plus doux au plus ferme (valeurs identiques à <c>ReminderLevelDto</c>). Les
/// niveaux de 1 à 4 se suivent (l'escalade) ; le rappel avant échéance et la relance d'une échéance d'échéancier manquée
/// sont à part : ils ne font pas monter le ton.
/// </summary>
public enum ReminderLevel
{
    /// <summary>Rappel amical avant l'échéance, aux clients dont la météo des paiements annonce pluie ou orage.</summary>
    BeforeDue = 0,

    /// <summary>Rappel amical, quelques jours après l'échéance.</summary>
    Friendly = 1,

    /// <summary>Relance.</summary>
    Reminder = 2,

    /// <summary>Dernière relance avant mise en demeure : pénalités et indemnité de 40 € citées.</summary>
    Final = 3,

    /// <summary>Mise en demeure de payer.</summary>
    FormalNotice = 4,

    /// <summary>Relance d'une échéance d'échéancier manquée : une seule par échéance.</summary>
    MissedInstallment = 5,
}

/// <summary>Les niveaux de l'escalade, dans l'ordre.</summary>
public static class ReminderLevels
{
    public static IReadOnlyList<ReminderLevel> Escalation { get; } =
        [ReminderLevel.Friendly, ReminderLevel.Reminder, ReminderLevel.Final, ReminderLevel.FormalNotice];

    /// <summary>Vrai pour les quatre niveaux qui se suivent, du rappel amical à la mise en demeure.</summary>
    public static bool IsEscalation(this ReminderLevel level) => level is >= ReminderLevel.Friendly and <= ReminderLevel.FormalNotice;
}

/// <summary>
/// Le rappel avant échéance et les bornes de l'échéancier en libre-service (« Avant le retard »). Mêmes bornes pour tous
/// les clients professionnels du dossier : jamais un accord selon une note.
/// </summary>
/// <param name="BeforeDueEnabled">Proposer le rappel avant échéance aux clients ciblés (pluie ou orage).</param>
/// <param name="BeforeDueDays">Combien de jours avant l'échéance (3 par défaut).</param>
/// <param name="PaymentPlansEnabled">L'espace client propose l'échéancier aux clients professionnels.</param>
/// <param name="PaymentPlanMaxInstallments">Échéances au plus (2 ou 3).</param>
/// <param name="PaymentPlanMinimumAmount">Reste dû TTC minimal d'une facture pour l'étaler.</param>
public sealed record ReminderPrevention(
    bool BeforeDueEnabled,
    int BeforeDueDays,
    bool PaymentPlansEnabled,
    int PaymentPlanMaxInstallments,
    decimal PaymentPlanMinimumAmount)
{
    public const int DefaultBeforeDueDays = 3;

    public const int MaxBeforeDueDays = 30;

    public const decimal DefaultPaymentPlanMinimumAmount = 300m;

    public const decimal MaxPaymentPlanMinimumAmount = 1_000_000m;

    /// <summary>Rappel avant échéance proposé à J−3 ; échéancier coupé tant que l'entreprise ne l'a pas ouvert.</summary>
    public static ReminderPrevention Default { get; } =
        new(true, DefaultBeforeDueDays, false, PaymentPlanRules.MaxInstallments, DefaultPaymentPlanMinimumAmount);

    public Result Validate()
    {
        if (BeforeDueDays is < 1 or > MaxBeforeDueDays)
        {
            return ReminderErrors.BeforeDueDaysInvalid;
        }

        bool installmentsValid = PaymentPlanMaxInstallments is >= PaymentPlanRules.MinInstallments and <= PaymentPlanRules.MaxInstallments;
        bool amountValid = PaymentPlanMinimumAmount is >= 0m and <= MaxPaymentPlanMinimumAmount && decimal.Round(PaymentPlanMinimumAmount, 2) == PaymentPlanMinimumAmount;
        return installmentsValid && amountValid ? Result.Success() : ReminderErrors.PaymentPlanBoundsInvalid;
    }
}

/// <summary>
/// Scénario de relance d'un dossier (une seule ligne par base de dossier, créée au premier réglage) : le nombre de jours
/// de retard qui déclenche chaque niveau, l'écart minimal entre deux relances d'une même facture, et l'envoi
/// automatique par le Worker, coupé par défaut (rien ne part chez un client sans que le cabinet l'ait décidé).
/// </summary>
public sealed class ReminderSettings : Entity<Guid>, IVersioned
{
    public static readonly Guid SingletonId = new("6c657474-7261-0000-0000-72656c616e63");

    /// <summary>J+3, J+15, J+30, J+45 (proposition de la vague 7).</summary>
    public static readonly IReadOnlyList<int> DefaultDelays = [3, 15, 30, 45];

    public const int DefaultMinimumGapDays = 7;

    public const int MaxDelayDays = 365;

    public const int MaxGapDays = 90;

    private ReminderSettings(Guid id)
        : base(id)
    {
    }

    /// <summary>Vrai : le Worker envoie chaque jour les relances dues aux clients qui ont une adresse e-mail.</summary>
    public bool AutoSend { get; private set; }

    public int FriendlyDays { get; private set; }

    public int ReminderDays { get; private set; }

    public int FinalDays { get; private set; }

    public int FormalNoticeDays { get; private set; }

    /// <summary>Jours au moins entre deux relances d'une même facture (une facture très en retard monte d'un niveau à la fois).</summary>
    public int MinimumGapDays { get; private set; }

    /// <summary>Rappel avant échéance proposé (et envoyé par la passe quotidienne si l'envoi automatique est activé).</summary>
    public bool BeforeDueEnabled { get; private set; }

    public int BeforeDueDays { get; private set; }

    /// <summary>L'espace client propose l'échéancier en libre-service aux clients professionnels.</summary>
    public bool PaymentPlansEnabled { get; private set; }

    public int PaymentPlanMaxInstallments { get; private set; }

    public decimal PaymentPlanMinimumAmount { get; private set; }

    /// <summary>Le rappel avant échéance et les bornes de l'échéancier, d'un bloc.</summary>
    public ReminderPrevention Prevention =>
        new(BeforeDueEnabled, BeforeDueDays, PaymentPlansEnabled, PaymentPlanMaxInstallments, PaymentPlanMinimumAmount);

    public long Version { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static ReminderSettings Default() => new(SingletonId)
    {
        AutoSend = false,
        FriendlyDays = DefaultDelays[0],
        ReminderDays = DefaultDelays[1],
        FinalDays = DefaultDelays[2],
        FormalNoticeDays = DefaultDelays[3],
        MinimumGapDays = DefaultMinimumGapDays,
        BeforeDueEnabled = ReminderPrevention.Default.BeforeDueEnabled,
        BeforeDueDays = ReminderPrevention.Default.BeforeDueDays,
        PaymentPlansEnabled = ReminderPrevention.Default.PaymentPlansEnabled,
        PaymentPlanMaxInstallments = ReminderPrevention.Default.PaymentPlanMaxInstallments,
        PaymentPlanMinimumAmount = ReminderPrevention.Default.PaymentPlanMinimumAmount,
        Version = 0,
    };

    /// <summary>Le nombre de jours de retard à partir duquel <paramref name="level"/> (un niveau de l'escalade) est dû.</summary>
    public int DelayOf(ReminderLevel level) => level switch
    {
        ReminderLevel.Friendly => FriendlyDays,
        ReminderLevel.Reminder => ReminderDays,
        ReminderLevel.Final => FinalDays,
        ReminderLevel.FormalNotice => FormalNoticeDays,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

    /// <summary>Le scénario d'escalade, l'envoi automatique, le rappel avant échéance et les bornes de l'échéancier.</summary>
    public Result Update(bool autoSend, IReadOnlyList<int> delays, int minimumGapDays, ReminderPrevention prevention, Guid userId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(delays);
        ArgumentNullException.ThrowIfNull(prevention);
        if (delays.Count != 4 || delays[0] < 1 || delays[^1] > MaxDelayDays || delays.Zip(delays.Skip(1)).Any(p => p.First >= p.Second))
        {
            return ReminderErrors.DelaysInvalid;
        }

        if (minimumGapDays is < 1 or > MaxGapDays)
        {
            return ReminderErrors.GapInvalid;
        }

        Result valid = prevention.Validate();
        if (valid.IsFailure)
        {
            return valid;
        }

        AutoSend = autoSend;
        (FriendlyDays, ReminderDays, FinalDays, FormalNoticeDays) = (delays[0], delays[1], delays[2], delays[3]);
        MinimumGapDays = minimumGapDays;
        (BeforeDueEnabled, BeforeDueDays) = (prevention.BeforeDueEnabled, prevention.BeforeDueDays);
        (PaymentPlansEnabled, PaymentPlanMaxInstallments, PaymentPlanMinimumAmount) =
            (prevention.PaymentPlansEnabled, prevention.PaymentPlanMaxInstallments, prevention.PaymentPlanMinimumAmount);
        UpdatedBy = userId;
        UpdatedAt = now;
        Version++;
        return Result.Success();
    }
}

/// <summary>Codes d'erreur des relances (vague 7).</summary>
public static class ReminderErrors
{
    public const string DelaysInvalidCode = "Reminders.DelaysInvalid";
    public const string GapInvalidCode = "Reminders.GapInvalid";
    public const string InvoiceNotOverdueCode = "Reminders.InvoiceNotOverdue";
    public const string CustomerEmailMissingCode = "Reminders.CustomerEmailMissing";
    public const string NoLevelLeftCode = "Reminders.NoLevelLeft";
    public const string CompanyProfileMissingCode = "Reminders.CompanyProfileMissing";
    public const string BeforeDueDaysInvalidCode = "Reminders.BeforeDueDaysInvalid";
    public const string PaymentPlanBoundsInvalidCode = "Reminders.PaymentPlanBoundsInvalid";

    public static Error DelaysInvalid => Error.Validation(DelaysInvalidCode, "Delays");

    public static Error GapInvalid => Error.Validation(GapInvalidCode, "MinimumGapDays");

    public static Error InvoiceNotOverdue => new(InvoiceNotOverdueCode, ErrorKind.Conflict);

    public static Error CustomerEmailMissing => new(CustomerEmailMissingCode, ErrorKind.Conflict);

    public static Error NoLevelLeft => new(NoLevelLeftCode, ErrorKind.Conflict);

    public static Error CompanyProfileMissing => new(CompanyProfileMissingCode, ErrorKind.Conflict);

    public static Error BeforeDueDaysInvalid => Error.Validation(BeforeDueDaysInvalidCode, "BeforeDueDays");

    public static Error PaymentPlanBoundsInvalid => Error.Validation(PaymentPlanBoundsInvalidCode, "PaymentPlanMinimumAmount");
}
