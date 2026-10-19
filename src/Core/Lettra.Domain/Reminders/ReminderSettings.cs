using Lettra.Domain.Common;

namespace Lettra.Domain.Reminders;

/// <summary>Niveaux du scénario de relance, du plus doux au plus ferme (valeurs identiques à <c>ReminderLevelDto</c>).</summary>
public enum ReminderLevel
{
    /// <summary>Rappel amical, quelques jours après l'échéance.</summary>
    Friendly = 1,

    /// <summary>Relance.</summary>
    Reminder = 2,

    /// <summary>Dernière relance avant mise en demeure : pénalités et indemnité de 40 € citées.</summary>
    Final = 3,

    /// <summary>Mise en demeure de payer.</summary>
    FormalNotice = 4,
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
        Version = 0,
    };

    /// <summary>Le nombre de jours de retard à partir duquel <paramref name="level"/> est dû.</summary>
    public int DelayOf(ReminderLevel level) => level switch
    {
        ReminderLevel.Friendly => FriendlyDays,
        ReminderLevel.Reminder => ReminderDays,
        ReminderLevel.Final => FinalDays,
        ReminderLevel.FormalNotice => FormalNoticeDays,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

    public Result Update(bool autoSend, IReadOnlyList<int> delays, int minimumGapDays, Guid userId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(delays);
        if (delays.Count != 4 || delays[0] < 1 || delays[^1] > MaxDelayDays || delays.Zip(delays.Skip(1)).Any(p => p.First >= p.Second))
        {
            return ReminderErrors.DelaysInvalid;
        }

        if (minimumGapDays is < 1 or > MaxGapDays)
        {
            return ReminderErrors.GapInvalid;
        }

        AutoSend = autoSend;
        (FriendlyDays, ReminderDays, FinalDays, FormalNoticeDays) = (delays[0], delays[1], delays[2], delays[3]);
        MinimumGapDays = minimumGapDays;
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

    public static Error DelaysInvalid => Error.Validation(DelaysInvalidCode, "Delays");

    public static Error GapInvalid => Error.Validation(GapInvalidCode, "MinimumGapDays");

    public static Error InvoiceNotOverdue => new(InvoiceNotOverdueCode, ErrorKind.Conflict);

    public static Error CustomerEmailMissing => new(CustomerEmailMissingCode, ErrorKind.Conflict);

    public static Error NoLevelLeft => new(NoLevelLeftCode, ErrorKind.Conflict);

    public static Error CompanyProfileMissing => new(CompanyProfileMissingCode, ErrorKind.Conflict);
}
