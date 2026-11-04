using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Reminders;

/// <summary>État enregistré d'un échéancier : en cours, ou arrêté par l'entreprise.</summary>
public enum PaymentPlanStatus
{
    Active = 1,

    /// <summary>L'entreprise y a mis fin : la facture reprend ses relances habituelles.</summary>
    Cancelled = 2,
}

/// <summary>Une échéance d'un échéancier : son numéro (à partir de 1), sa date et son montant TTC.</summary>
public sealed record PaymentPlanInstallment(int Number, DateOnly DueDate, decimal Amount);

/// <summary>
/// Ce que l'échéancier doit savoir de la facture et de son client au moment de la demande. <paramref name="Disputed"/> :
/// la facture est contestée (litige déclaré sur la plateforme agréée) ; <paramref name="HasActivePlan"/> : elle a déjà
/// un échéancier en cours.
/// </summary>
public sealed record PaymentPlanSubject(
    InvoiceId InvoiceId,
    CustomerId CustomerId,
    string InvoiceNumber,
    InvoiceKind Kind,
    DateOnly IssueDate,
    decimal Outstanding,
    bool CustomerIsProfessional,
    bool Disputed,
    bool HasActivePlan);

/// <summary>
/// Les règles de l'échéancier en libre-service, les mêmes pour tous les clients : jamais un accord selon une note.
/// <list type="bullet">
/// <item>réservé aux clients professionnels dans le prototype (pour un particulier, un étalement peut relever du crédit à
/// la consommation : à valider par un juriste) ;</item>
/// <item>une facture ou un acompte encore dû, pas contesté, sans autre échéancier en cours, d'un reste dû au moins égal
/// au minimum de l'entreprise ;</item>
/// <item>de 2 au nombre d'échéances permis par l'entreprise (3 au plus), espacées régulièrement d'une semaine à un mois,
/// la dernière au plus tard au plafond légal des délais de paiement entre professionnels (Code de commerce,
/// art. L441-10) ;</item>
/// <item>des montants égaux au centime près, la dernière échéance prenant l'arrondi.</item>
/// </list>
/// </summary>
public static class PaymentPlanRules
{
    public const int MinInstallments = 2;

    public const int MaxInstallments = 3;

    public const int MinimumStepDays = 7;

    public const int MaximumStepDays = 30;

    /// <summary>Succès si la facture peut être étalée, sinon la première raison qui l'en empêche.</summary>
    public static Result Eligibility(PaymentPlanSubject subject, ReminderPrevention bounds)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(bounds);
        if (!bounds.PaymentPlansEnabled)
        {
            return PaymentPlanErrors.Disabled;
        }

        if (!subject.CustomerIsProfessional)
        {
            return PaymentPlanErrors.NotProfessional;
        }

        if (subject.Kind == InvoiceKind.CreditNote || subject.Outstanding <= 0m)
        {
            return PaymentPlanErrors.NothingDue;
        }

        if (subject.Disputed)
        {
            return PaymentPlanErrors.Disputed;
        }

        if (subject.HasActivePlan)
        {
            return PaymentPlanErrors.AlreadyPlanned;
        }

        return subject.Outstanding < bounds.PaymentPlanMinimumAmount ? PaymentPlanErrors.BelowMinimum : Result.Success();
    }

    /// <summary>Le calendrier proposé pour <paramref name="count"/> échéances à partir d'aujourd'hui.</summary>
    public static Result<IReadOnlyList<PaymentPlanInstallment>> Schedule(PaymentPlanSubject subject, ReminderPrevention bounds, int count, DateOnly today)
    {
        Result eligible = Eligibility(subject, bounds);
        if (eligible.IsFailure)
        {
            return Result<IReadOnlyList<PaymentPlanInstallment>>.Failure(eligible.Errors);
        }

        if (count < MinInstallments || count > bounds.PaymentPlanMaxInstallments)
        {
            return PaymentPlanErrors.InstallmentsInvalid;
        }

        int room = PaymentDelays.LegalCeiling(subject.IssueDate).DayNumber - today.DayNumber;
        int step = Math.Min(MaximumStepDays, room / count);
        if (step < MinimumStepDays)
        {
            return PaymentPlanErrors.NoRoom;
        }

        decimal share = Math.Floor(subject.Outstanding / count * 100m) / 100m;
        return Result<IReadOnlyList<PaymentPlanInstallment>>.Success(
        [
            .. Enumerable.Range(1, count).Select(n => new PaymentPlanInstallment(
                n, today.AddDays(step * n), n < count ? share : subject.Outstanding - (share * (count - 1)))),
        ]);
    }

    /// <summary>Les calendriers possibles, de 2 échéances au maximum permis ; vide si la facture ne peut pas être étalée.</summary>
    public static IReadOnlyList<IReadOnlyList<PaymentPlanInstallment>> Options(PaymentPlanSubject subject, ReminderPrevention bounds, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        return
        [
            .. Enumerable.Range(MinInstallments, Math.Max(0, bounds.PaymentPlanMaxInstallments - MinInstallments + 1))
                .Select(count => Schedule(subject, bounds, count, today))
                .Where(r => r.IsSuccess)
                .Select(r => r.Value),
        ];
    }
}

/// <summary>
/// Un échéancier demandé par le client dans son espace client, dans les bornes fixées par l'entreprise. Lettra le crée
/// sans attendre (les bornes valent accord) et prévient l'entreprise, qui le prend en compte ou y met fin. Tant qu'il
/// court, les relances habituelles de la facture s'arrêtent : une seule relance par échéance manquée. Les montants et les
/// dates ne changent jamais ; le suivi se lit sur le reste dû de la facture.
/// </summary>
public sealed class PaymentPlan : Entity<Guid>, IVersioned
{
    public const int MaxRequesterLength = 120;

    /// <summary>Tolérance d'arrondi pour dire une échéance réglée.</summary>
    private const decimal Cent = 0.005m;

    private readonly List<PaymentPlanInstallment> _installments = [];

    private PaymentPlan(Guid id)
        : base(id)
    {
    }

    public InvoiceId InvoiceId { get; private init; }

    public CustomerId CustomerId { get; private init; }

    public string InvoiceNumber { get; private init; } = "";

    public PaymentPlanStatus Status { get; private set; }

    /// <summary>Le reste dû de la facture à la demande : la somme des échéances.</summary>
    public decimal Total { get; private init; }

    public IReadOnlyList<PaymentPlanInstallment> Installments => _installments;

    /// <summary>Nom saisi par la personne qui a demandé l'échéancier, avec son accord.</summary>
    public string RequestedBy { get; private init; } = "";

    public DateTimeOffset RequestedAt { get; private init; }

    /// <summary>Le lien d'espace client d'où vient la demande.</summary>
    public Guid? PortalLinkId { get; private init; }

    /// <summary>Le compte sous lequel l'échéancier a été créé (celui de l'espace client).</summary>
    public Guid CreatedBy { get; private init; }

    /// <summary>L'entreprise a vu l'échéancier : l'alerte du dossier se lève tant qu'un échéancier n'est pas vu.</summary>
    public DateTimeOffset? AcknowledgedAt { get; private set; }

    public Guid? AcknowledgedBy { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public Guid? CancelledBy { get; private set; }

    public long Version { get; private set; }

    public DateOnly FinalDueDate => _installments[^1].DueDate;

    public bool IsAcknowledged => AcknowledgedAt is not null;

    /// <summary>Crée l'échéancier demandé : bornes de l'entreprise, nom de la personne et son accord.</summary>
    public static Result<PaymentPlan> Request(
        PaymentPlanSubject subject,
        ReminderPrevention bounds,
        int installments,
        string? requesterName,
        bool consent,
        Guid? portalLink,
        Guid userId,
        DateOnly today,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(subject);
        Result<IReadOnlyList<PaymentPlanInstallment>> schedule = PaymentPlanRules.Schedule(subject, bounds, installments, today);
        if (schedule.IsFailure)
        {
            return Result<PaymentPlan>.Failure(schedule.Errors);
        }

        string name = string.Join(' ', (requesterName ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length is < 2 or > MaxRequesterLength)
        {
            return PaymentPlanErrors.RequesterInvalid;
        }

        if (!consent)
        {
            return PaymentPlanErrors.ConsentRequired;
        }

        PaymentPlan plan = new(Guid.CreateVersion7())
        {
            InvoiceId = subject.InvoiceId,
            CustomerId = subject.CustomerId,
            InvoiceNumber = subject.InvoiceNumber,
            Status = PaymentPlanStatus.Active,
            Total = subject.Outstanding,
            RequestedBy = name,
            RequestedAt = now,
            PortalLinkId = portalLink,
            CreatedBy = userId,
        };
        plan._installments.AddRange(schedule.Value);
        return plan;
    }

    /// <summary>L'entreprise a vu l'échéancier. Sans effet s'il l'était déjà.</summary>
    public void Acknowledge(Guid userId, DateTimeOffset now)
    {
        if (AcknowledgedAt is not null)
        {
            return;
        }

        (AcknowledgedAt, AcknowledgedBy) = (now, userId);
        Version++;
    }

    /// <summary>L'entreprise met fin à l'échéancier : la facture reprend ses relances habituelles.</summary>
    public Result Cancel(Guid userId, DateTimeOffset now)
    {
        if (Status == PaymentPlanStatus.Cancelled)
        {
            return PaymentPlanErrors.AlreadyCancelled;
        }

        Status = PaymentPlanStatus.Cancelled;
        (CancelledAt, CancelledBy) = (now, userId);
        AcknowledgedAt ??= now;
        AcknowledgedBy ??= userId;
        Version++;
        return Result.Success();
    }

    /// <summary>
    /// La date de la prochaine échéance pas encore couverte par les règlements (la dernière si tout est couvert) : la
    /// date à laquelle la facture est attendue tant que l'échéancier court.
    /// </summary>
    public DateOnly NextDueDate(decimal outstanding) => Progress(outstanding, DateOnly.MinValue).Next?.Installment.DueDate ?? FinalDueDate;

    /// <summary>
    /// Où en est l'échéancier, lu sur le reste dû de la facture : ce qui a été réglé depuis la demande couvre les
    /// échéances dans l'ordre ; une échéance non couverte dont la date est passée est manquée.
    /// </summary>
    public PaymentPlanProgress Progress(decimal outstanding, DateOnly today)
    {
        decimal covered = Math.Clamp(Total - Math.Max(0m, outstanding), 0m, Total);
        decimal cumulative = 0m;
        List<PaymentPlanInstallmentState> states = [];
        foreach (PaymentPlanInstallment installment in _installments)
        {
            cumulative += installment.Amount;
            bool paid = cumulative <= covered + Cent;
            decimal remaining = paid ? 0m : Math.Min(installment.Amount, cumulative - covered);
            states.Add(new PaymentPlanInstallmentState(installment, cumulative, paid, !paid && installment.DueDate < today, remaining));
        }

        bool completed = outstanding <= 0m;
        bool lapsed = Status == PaymentPlanStatus.Active && !completed && today > FinalDueDate;
        return new PaymentPlanProgress(covered, states, completed, lapsed);
    }
}

/// <summary>Une échéance et son suivi : réglée, manquée (sa date est passée sans qu'elle soit couverte), ce qu'il en reste à payer.</summary>
public sealed record PaymentPlanInstallmentState(PaymentPlanInstallment Installment, decimal Cumulative, bool Paid, bool Missed, decimal Remaining);

/// <summary>
/// Le suivi d'un échéancier à une date. <paramref name="Covered"/> : ce qui a été réglé (ou avoir) depuis la demande ;
/// <paramref name="Completed"/> : la facture est soldée ; <paramref name="Lapsed"/> : la dernière échéance est passée et
/// la facture n'est pas soldée, l'échéancier n'a pas été tenu.
/// </summary>
public sealed record PaymentPlanProgress(decimal Covered, IReadOnlyList<PaymentPlanInstallmentState> Installments, bool Completed, bool Lapsed)
{
    /// <summary>La prochaine échéance à régler.</summary>
    public PaymentPlanInstallmentState? Next => Installments.FirstOrDefault(i => !i.Paid);

    /// <summary>La dernière échéance manquée.</summary>
    public PaymentPlanInstallmentState? LastMissed => Installments.LastOrDefault(i => i.Missed);

    /// <summary>Ce qui manque aux échéances passées.</summary>
    public decimal AmountLate => LastMissed is { } missed ? Math.Max(0m, missed.Cumulative - Covered) : 0m;

    public int PaidCount => Installments.Count(i => i.Paid);
}

/// <summary>Opposition d'un client au ciblage selon son comportement de paiement (RGPD, art. 21).</summary>
/// <remarks>
/// Le rappel avant échéance n'est plus proposé à ce client ; les relances après l'échéance, qui ne ciblent personne,
/// ne changent pas. L'opposition se lève en la retirant.
/// </remarks>
public sealed class PaymentTargetingObjection : Entity<Guid>
{
    private PaymentTargetingObjection(Guid id)
        : base(id)
    {
    }

    public CustomerId CustomerId { get; private init; }

    public DateTimeOffset RecordedAt { get; private init; }

    public Guid RecordedBy { get; private init; }

    public static PaymentTargetingObjection Record(CustomerId customer, Guid userId, DateTimeOffset now) =>
        new(Guid.CreateVersion7()) { CustomerId = customer, RecordedAt = now, RecordedBy = userId };
}

/// <summary>Codes d'erreur de l'échéancier en libre-service.</summary>
public static class PaymentPlanErrors
{
    public const string DisabledCode = "PaymentPlans.Disabled";
    public const string NotProfessionalCode = "PaymentPlans.NotProfessional";
    public const string NothingDueCode = "PaymentPlans.NothingDue";
    public const string DisputedCode = "PaymentPlans.Disputed";
    public const string AlreadyPlannedCode = "PaymentPlans.AlreadyPlanned";
    public const string BelowMinimumCode = "PaymentPlans.BelowMinimum";
    public const string NoRoomCode = "PaymentPlans.NoRoom";
    public const string InstallmentsInvalidCode = "PaymentPlans.InstallmentsInvalid";
    public const string RequesterInvalidCode = "PaymentPlans.RequesterInvalid";
    public const string ConsentRequiredCode = "PaymentPlans.ConsentRequired";
    public const string NotFoundCode = "PaymentPlans.NotFound";
    public const string AlreadyCancelledCode = "PaymentPlans.AlreadyCancelled";

    /// <summary>L'entreprise ne propose pas l'échéancier.</summary>
    public static Error Disabled => Error.Conflict(DisabledCode);

    /// <summary>Réservé aux clients professionnels dans le prototype.</summary>
    public static Error NotProfessional => Error.Conflict(NotProfessionalCode);

    public static Error NothingDue => Error.Conflict(NothingDueCode);

    public static Error Disputed => Error.Conflict(DisputedCode);

    public static Error AlreadyPlanned => Error.Conflict(AlreadyPlannedCode);

    public static Error BelowMinimum => Error.Conflict(BelowMinimumCode);

    /// <summary>Le plafond légal des délais de paiement est trop proche pour étaler la facture.</summary>
    public static Error NoRoom => Error.Conflict(NoRoomCode);

    public static Error InstallmentsInvalid => Error.Validation(InstallmentsInvalidCode, "Installments");

    public static Error RequesterInvalid => Error.Validation(RequesterInvalidCode, "RequesterName");

    public static Error ConsentRequired => Error.Validation(ConsentRequiredCode, "Consent");

    public static Error NotFound => Error.NotFound(NotFoundCode);

    public static Error AlreadyCancelled => Error.Conflict(AlreadyCancelledCode);
}
