using Lettra.Domain.Common;

namespace Lettra.Domain.Missions;

/// <summary>Nature de la mission, d'après les normes de la profession.</summary>
public enum MissionKind
{
    /// <summary>Présentation des comptes annuels (norme NP 2300) : la mission la plus courante d'un cabinet.</summary>
    Presentation = 1,

    /// <summary>Tenue de la comptabilité seule, sans présentation des comptes.</summary>
    Bookkeeping = 2,

    /// <summary>Examen limité des comptes (assurance modérée).</summary>
    Review = 3,
}

/// <summary>Les travaux compris dans la mission ; chaque travail est cité dans la lettre.</summary>
[Flags]
public enum MissionServices
{
    None = 0,
    Bookkeeping = 1,
    VatReturns = 2,
    AnnualAccounts = 4,
    TaxReturns = 8,
    Payroll = 16,
    Legal = 32,
    All = Bookkeeping | VatReturns | AnnualAccounts | TaxReturns | Payroll | Legal,
}

/// <summary>Rythme de facturation du forfait annuel.</summary>
public enum FeeSchedule
{
    Monthly = 1,
    Quarterly = 2,
    Yearly = 3,
}

/// <summary>
/// La mission du cabinet pour ce dossier : ce que la lettre de mission engage (nature, travaux, début), le forfait
/// annuel d'honoraires et son rythme de facturation, et le taux horaire visé qui donne le budget d'heures. Unique dans
/// la base du dossier, comme la fiche de l'entreprise.
/// </summary>
public sealed class Mission : AggregateRoot<Guid>, IVersioned
{
    /// <summary>Identifiant fixe : la mission est unique dans la base du dossier.</summary>
    public static readonly Guid SingletonId = new("6c657474-7261-0000-0000-6d697373696f");

    public const int MaxNameLength = 120;
    public const decimal MaxAnnualFee = 10_000_000m;
    public const decimal MaxHourlyRate = 10_000m;

    // Constructeur réservé à la persistance.
    private Mission(Guid id)
        : base(id)
    {
        ClientSignatory = string.Empty;
    }

    public MissionKind Kind { get; private set; }

    public MissionServices Services { get; private set; }

    public DateOnly StartDate { get; private set; }

    public decimal AnnualFeeExclTax { get; private set; }

    public FeeSchedule Schedule { get; private set; }

    /// <summary>Le taux horaire que le cabinet vise pour ce dossier ; il donne le budget d'heures. Facultatif.</summary>
    public decimal? TargetHourlyRate { get; private set; }

    /// <summary>Qui signe pour le client (le dirigeant).</summary>
    public string ClientSignatory { get; private set; }

    /// <summary>L'expert-comptable responsable de la mission, qui signe pour le cabinet.</summary>
    public string? Manager { get; private set; }

    /// <summary>Date de signature de la lettre ; vide tant qu'elle n'est pas signée.</summary>
    public DateOnly? SignedOn { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public long Version { get; private set; }

    /// <summary>Le montant de chaque facture du forfait, arrondi au centime.</summary>
    public decimal FeePerInstalment => Math.Round(AnnualFeeExclTax / InstalmentsPerYear(Schedule), 2, MidpointRounding.AwayFromZero);

    /// <summary>Le nombre d'heures que le forfait paie au taux visé, à une décimale ; vide sans taux visé.</summary>
    public decimal? BudgetHours => TargetHourlyRate is > 0m and { } rate ? Math.Round(AnnualFeeExclTax / rate, 1, MidpointRounding.AwayFromZero) : null;

    public static int InstalmentsPerYear(FeeSchedule schedule) => schedule switch
    {
        FeeSchedule.Monthly => 12,
        FeeSchedule.Quarterly => 4,
        _ => 1,
    };

    public static Result<Mission> Create(Draft draft, DateTimeOffset now)
    {
        Mission mission = new(SingletonId);
        Result applied = mission.Update(draft, now);
        return applied.IsFailure ? Result<Mission>.Failure(applied.Errors) : mission;
    }

    public Result Update(Draft draft, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        List<Error> errors = [.. Validate(draft)];
        if (errors.Count > 0)
        {
            return Result.Failure(errors);
        }

        Kind = draft.Kind;
        Services = draft.Services;
        StartDate = draft.StartDate;
        AnnualFeeExclTax = draft.AnnualFeeExclTax;
        Schedule = draft.Schedule;
        TargetHourlyRate = draft.TargetHourlyRate is > 0m ? draft.TargetHourlyRate : null;
        ClientSignatory = draft.ClientSignatory.Trim();
        Manager = string.IsNullOrWhiteSpace(draft.Manager) ? null : draft.Manager.Trim();
        SignedOn = draft.SignedOn;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// L'année de mission qui contient <paramref name="day"/> : elle commence à la date anniversaire du début (un
    /// 29 février devient le 28 les autres années). Avant le début, c'est la première année.
    /// </summary>
    public (DateOnly From, DateOnly To) YearOf(DateOnly day)
    {
        DateOnly from = StartDate;
        while (from.AddYears(1) <= day)
        {
            from = from.AddYears(1);
        }

        return (from, from.AddYears(1).AddDays(-1));
    }

    private static IEnumerable<Error> Validate(Draft draft)
    {
        if (!Enum.IsDefined(draft.Kind))
        {
            yield return MissionErrors.KindInvalid;
        }

        if (draft.Services == MissionServices.None || (draft.Services & ~MissionServices.All) != 0)
        {
            yield return MissionErrors.ServicesInvalid;
        }

        if (draft.AnnualFeeExclTax is <= 0m or > MaxAnnualFee || decimal.Round(draft.AnnualFeeExclTax, 2) != draft.AnnualFeeExclTax)
        {
            yield return MissionErrors.FeeInvalid;
        }

        if (!Enum.IsDefined(draft.Schedule))
        {
            yield return MissionErrors.ScheduleInvalid;
        }

        if (draft.TargetHourlyRate is < 0m or > MaxHourlyRate)
        {
            yield return MissionErrors.RateInvalid;
        }

        if (string.IsNullOrWhiteSpace(draft.ClientSignatory))
        {
            yield return MissionErrors.SignatoryRequired;
        }
        else if (draft.ClientSignatory.Trim().Length > MaxNameLength || draft.Manager?.Trim().Length > MaxNameLength)
        {
            yield return MissionErrors.NameTooLong;
        }
    }

    /// <summary>Ce que l'écran envoie : toute la mission, d'un coup.</summary>
    public sealed record Draft(
        MissionKind Kind,
        MissionServices Services,
        DateOnly StartDate,
        decimal AnnualFeeExclTax,
        FeeSchedule Schedule,
        decimal? TargetHourlyRate,
        string ClientSignatory,
        string? Manager,
        DateOnly? SignedOn);
}

/// <summary>Nature d'un temps passé, pour voir où vont les heures.</summary>
public enum TimeTask
{
    Bookkeeping = 1,
    Vat = 2,
    Closing = 3,
    Payroll = 4,
    Legal = 5,
    Advice = 6,
    Other = 7,
}

/// <summary>Un temps passé sur le dossier par un collaborateur du cabinet : jour, durée, nature, note facultative.</summary>
public sealed class TimeEntry : Entity<Guid>
{
    /// <summary>Douze heures au plus par saisie.</summary>
    public const int MaxMinutes = 12 * 60;
    public const int MaxNoteLength = 200;

    private TimeEntry(Guid id, DateOnly date, Guid userId, int minutes, TimeTask task, string? note, DateTimeOffset createdAt)
        : base(id)
    {
        Date = date;
        UserId = userId;
        Minutes = minutes;
        Task = task;
        Note = note;
        CreatedAt = createdAt;
    }

    public DateOnly Date { get; private set; }

    public Guid UserId { get; private set; }

    public int Minutes { get; private set; }

    public TimeTask Task { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Un temps ne se saisit pas à l'avance : <paramref name="today"/> est la date légale du jour.</summary>
    public static Result<TimeEntry> Create(Guid id, DateOnly date, Guid userId, int minutes, TimeTask task, string? note, DateOnly today, DateTimeOffset now)
    {
        string? trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        List<Error> errors = [];
        if (minutes is <= 0 or > MaxMinutes)
        {
            errors.Add(MissionErrors.DurationInvalid);
        }

        if (!Enum.IsDefined(task))
        {
            errors.Add(MissionErrors.TaskInvalid);
        }

        if (date > today)
        {
            errors.Add(MissionErrors.DateInFuture);
        }

        if (trimmed?.Length > MaxNoteLength)
        {
            errors.Add(MissionErrors.NoteTooLong);
        }

        return errors.Count > 0 ? Result<TimeEntry>.Failure(errors) : new TimeEntry(id, date, userId, minutes, task, trimmed, now);
    }
}

public static class MissionErrors
{
    public const string NotFoundCode = "Mission.NotFound";
    public const string KindInvalidCode = "Mission.KindInvalid";
    public const string ServicesInvalidCode = "Mission.ServicesInvalid";
    public const string FeeInvalidCode = "Mission.FeeInvalid";
    public const string ScheduleInvalidCode = "Mission.ScheduleInvalid";
    public const string RateInvalidCode = "Mission.RateInvalid";
    public const string SignatoryRequiredCode = "Mission.SignatoryRequired";
    public const string NameTooLongCode = "Mission.NameTooLong";
    public const string DurationInvalidCode = "Mission.DurationInvalid";
    public const string TaskInvalidCode = "Mission.TaskInvalid";
    public const string DateInFutureCode = "Mission.DateInFuture";
    public const string NoteTooLongCode = "Mission.NoteTooLong";
    public const string TimeEntryNotFoundCode = "Mission.TimeEntryNotFound";
    public const string TimeEntryNotYoursCode = "Mission.TimeEntryNotYours";

    public static Error NotFound => Error.NotFound(NotFoundCode);

    public static Error KindInvalid => Error.Validation(KindInvalidCode, "Kind");

    public static Error ServicesInvalid => Error.Validation(ServicesInvalidCode, "Services");

    public static Error FeeInvalid => Error.Validation(FeeInvalidCode, "AnnualFeeExclTax");

    public static Error ScheduleInvalid => Error.Validation(ScheduleInvalidCode, "Schedule");

    public static Error RateInvalid => Error.Validation(RateInvalidCode, "TargetHourlyRate");

    public static Error SignatoryRequired => Error.Validation(SignatoryRequiredCode, "ClientSignatory");

    public static Error NameTooLong => Error.Validation(NameTooLongCode, "ClientSignatory");

    public static Error DurationInvalid => Error.Validation(DurationInvalidCode, "Minutes");

    public static Error TaskInvalid => Error.Validation(TaskInvalidCode, "Task");

    public static Error DateInFuture => Error.Validation(DateInFutureCode, "Date");

    public static Error NoteTooLong => Error.Validation(NoteTooLongCode, "Note");

    public static Error TimeEntryNotFound => Error.NotFound(TimeEntryNotFoundCode);

    public static Error TimeEntryNotYours => new(TimeEntryNotYoursCode, ErrorKind.Forbidden);
}
