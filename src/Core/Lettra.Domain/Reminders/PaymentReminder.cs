using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Reminders;

/// <summary>Comment la relance est partie (valeurs identiques à <c>ReminderChannelDto</c>).</summary>
public enum ReminderChannel
{
    /// <summary>Envoyée par Lettra à l'adresse e-mail du client.</summary>
    Email = 1,

    /// <summary>Envoyée par le cabinet lui-même (courrier, téléphone, sa messagerie), puis notée dans Lettra.</summary>
    Manual = 2,
}

/// <summary>Ce qu'il est advenu de la relance (valeurs identiques à <c>ReminderOutcomeDto</c>).</summary>
public enum ReminderOutcome
{
    Sent = 1,

    /// <summary>L'envoi par e-mail a échoué : gardé dans l'historique, il ne compte pas comme une relance faite.</summary>
    Failed = 2,
}

/// <summary>Qui a déclenché la relance (valeurs identiques à <c>ReminderTriggerDto</c>).</summary>
public enum ReminderTrigger
{
    User = 1,

    /// <summary>La passe quotidienne du Worker (envoi automatique activé).</summary>
    Schedule = 2,
}

/// <summary>
/// Une relance de facture, gardée telle qu'envoyée : niveau, canal, destinataire, objet et texte, montant dû et retard au
/// moment de l'envoi. Rien n'est jamais modifié ni supprimé : c'est la trace à montrer au client ou au juge.
/// </summary>
public sealed class PaymentReminder : Entity<Guid>
{
    public const int MaxSubjectLength = 300;

    public const int MaxBodyLength = 8000;

    private PaymentReminder(Guid id)
        : base(id)
    {
    }

    public InvoiceId InvoiceId { get; private init; }

    public CustomerId CustomerId { get; private init; }

    public string InvoiceNumber { get; private init; } = "";

    public ReminderLevel Level { get; private init; }

    public ReminderChannel Channel { get; private init; }

    public ReminderOutcome Outcome { get; private init; }

    /// <summary>Code de l'erreur d'envoi (<c>Email.SendFailed</c>…), seulement si l'envoi a échoué.</summary>
    public string? ErrorCode { get; private init; }

    /// <summary>Adresse e-mail du client pour un envoi par Lettra ; absente pour une relance notée à la main.</summary>
    public string? Recipient { get; private init; }

    /// <summary><c>fr-FR</c> ou <c>en-US</c>, la langue du client.</summary>
    public string Language { get; private init; } = "fr-FR";

    public string Subject { get; private init; } = "";

    public string Body { get; private init; } = "";

    public decimal Outstanding { get; private init; }

    public int DaysOverdue { get; private init; }

    public ReminderTrigger Trigger { get; private init; }

    /// <summary>L'utilisateur, ou le compte de service du Worker.</summary>
    public Guid UserId { get; private init; }

    public DateTimeOffset OccurredAt { get; private init; }

    /// <summary>Jour civil (Paris) de l'envoi, pour l'écart minimal entre deux relances.</summary>
    public DateOnly OccurredOn { get; private init; }

    /// <summary>Une relance partie (ou notée), ou un envoi par e-mail qui a échoué (<paramref name="errorCode"/> renseigné).</summary>
    public static PaymentReminder Record(
        ReminderDraft draft,
        ReminderChannel channel,
        string? recipient,
        string? errorCode,
        ReminderTrigger trigger,
        Guid userId,
        DateTimeOffset now,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return new PaymentReminder(Guid.CreateVersion7())
        {
            InvoiceId = draft.InvoiceId,
            CustomerId = draft.CustomerId,
            InvoiceNumber = draft.InvoiceNumber,
            Level = draft.Level,
            Channel = channel,
            Outcome = errorCode is null ? ReminderOutcome.Sent : ReminderOutcome.Failed,
            ErrorCode = errorCode,
            Recipient = recipient,
            Language = draft.Language,
            Subject = Truncate(draft.Subject, MaxSubjectLength),
            Body = Truncate(draft.Body, MaxBodyLength),
            Outstanding = draft.Outstanding,
            DaysOverdue = draft.DaysOverdue,
            Trigger = trigger,
            UserId = userId,
            OccurredAt = now,
            OccurredOn = today,
        };
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>Une relance prête à partir : la facture, le niveau, et le texte dans la langue du client.</summary>
public sealed record ReminderDraft(
    InvoiceId InvoiceId,
    CustomerId CustomerId,
    string InvoiceNumber,
    ReminderLevel Level,
    string Language,
    string Subject,
    string Body,
    decimal Outstanding,
    int DaysOverdue);
