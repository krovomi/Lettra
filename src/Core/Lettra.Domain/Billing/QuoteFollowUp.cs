using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Billing;

/// <summary>Comment la relance du devis est partie (valeurs identiques à <c>QuoteFollowUpChannelDto</c>).</summary>
public enum QuoteFollowUpChannel
{
    /// <summary>Envoyée par Lettra à l'adresse e-mail du client.</summary>
    Email = 1,

    /// <summary>Faite par l'utilisateur lui-même (téléphone, rendez-vous, sa messagerie), puis notée dans Lettra.</summary>
    Manual = 2,
}

/// <summary>Ce qu'il est advenu de la relance (valeurs identiques à <c>QuoteFollowUpOutcomeDto</c>).</summary>
public enum QuoteFollowUpOutcome
{
    Sent = 1,

    /// <summary>L'envoi par e-mail a échoué : gardé dans l'historique, il ne compte pas comme une relance faite.</summary>
    Failed = 2,
}

/// <summary>Une relance de devis prête à partir : le devis, et le texte relu par l'utilisateur, dans la langue du client.</summary>
public sealed record QuoteFollowUpDraft(
    QuoteId QuoteId,
    CustomerId CustomerId,
    string QuoteNumber,
    string Language,
    string Subject,
    string Body,
    decimal AmountExclTax,
    DateOnly ValidUntil);

/// <summary>
/// Une relance de devis, gardée telle qu'envoyée : canal, destinataire, objet et texte, montant et fin de validité au
/// moment de l'envoi. Rien n'est jamais modifié ni supprimé : c'est l'historique des relances du devis.
/// </summary>
public sealed class QuoteFollowUp : Entity<Guid>
{
    public const int MaxSubjectLength = 300;

    public const int MaxBodyLength = 8000;

    private QuoteFollowUp(Guid id)
        : base(id)
    {
    }

    public QuoteId QuoteId { get; private init; }

    public CustomerId CustomerId { get; private init; }

    public string QuoteNumber { get; private init; } = "";

    public QuoteFollowUpChannel Channel { get; private init; }

    public QuoteFollowUpOutcome Outcome { get; private init; }

    /// <summary>Code de l'erreur d'envoi (<c>Email.SendFailed</c>…), seulement si l'envoi a échoué.</summary>
    public string? ErrorCode { get; private init; }

    /// <summary>Adresse e-mail du client pour un envoi par Lettra ; absente pour une relance notée à la main.</summary>
    public string? Recipient { get; private init; }

    /// <summary><c>fr-FR</c> ou <c>en-US</c>, la langue du client.</summary>
    public string Language { get; private init; } = DocumentLanguages.French;

    public string Subject { get; private init; } = "";

    public string Body { get; private init; } = "";

    /// <summary>Montant HT du devis au moment de la relance.</summary>
    public decimal AmountExclTax { get; private init; }

    /// <summary>Fin de validité du devis au moment de la relance.</summary>
    public DateOnly ValidUntil { get; private init; }

    public Guid UserId { get; private init; }

    public DateTimeOffset OccurredAt { get; private init; }

    /// <summary>Jour civil (Paris) de la relance : un devis relancé depuis qu'il approche de sa fin n'est plus « à relancer ».</summary>
    public DateOnly OccurredOn { get; private init; }

    /// <summary>Une relance partie (ou notée), ou un envoi par e-mail qui a échoué (<paramref name="errorCode"/> renseigné).</summary>
    public static QuoteFollowUp Record(
        QuoteFollowUpDraft draft,
        QuoteFollowUpChannel channel,
        string? recipient,
        string? errorCode,
        Guid userId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return new QuoteFollowUp(Guid.CreateVersion7())
        {
            QuoteId = draft.QuoteId,
            CustomerId = draft.CustomerId,
            QuoteNumber = draft.QuoteNumber,
            Channel = channel,
            Outcome = errorCode is null ? QuoteFollowUpOutcome.Sent : QuoteFollowUpOutcome.Failed,
            ErrorCode = errorCode,
            Recipient = recipient,
            Language = draft.Language,
            Subject = Truncate(draft.Subject, MaxSubjectLength),
            Body = Truncate(draft.Body, MaxBodyLength),
            AmountExclTax = draft.AmountExclTax,
            ValidUntil = draft.ValidUntil,
            UserId = userId,
            OccurredAt = now,
            OccurredOn = LegalCalendar.DateOf(now),
        };
    }

    /// <summary>Un objet et un texte non vides, dans les longueurs gardées par l'historique.</summary>
    public static Result CheckMessage(string? subject, string? body)
    {
        List<Error> errors = [];
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > MaxSubjectLength || subject.Contains('\n', StringComparison.Ordinal))
        {
            errors.Add(QuoteFollowUpErrors.MessageInvalid.For("Subject"));
        }

        if (string.IsNullOrWhiteSpace(body) || body.Length > MaxBodyLength)
        {
            errors.Add(QuoteFollowUpErrors.MessageInvalid.For("Body"));
        }

        return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>
/// Réglage des devis à relancer (une seule ligne par base de dossier, créée au premier réglage) : combien de jours avant
/// la fin de validité un devis sans réponse devient « à relancer ». Cinq jours par défaut.
/// </summary>
public sealed class QuoteFollowUpSettings : Entity<Guid>, IVersioned
{
    public static readonly Guid SingletonId = new("6c657474-7261-0000-0000-646576697321");

    public const int DefaultWindowDays = 5;

    public const int MinWindowDays = 1;

    /// <summary>Au-delà, la fenêtre couvre toute la validité par défaut (30 jours) : la liste perdrait son sens.</summary>
    public const int MaxWindowDays = 30;

    private QuoteFollowUpSettings(Guid id)
        : base(id)
    {
    }

    /// <summary>Un devis sans réponse dont la fin de validité tombe dans ce nombre de jours est à relancer.</summary>
    public int WindowDays { get; private set; }

    public long Version { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static QuoteFollowUpSettings Default() => new(SingletonId) { WindowDays = DefaultWindowDays, Version = 0 };

    public Result Update(int windowDays, Guid userId, DateTimeOffset now)
    {
        if (windowDays is < MinWindowDays or > MaxWindowDays)
        {
            return QuoteFollowUpErrors.WindowInvalid;
        }

        WindowDays = windowDays;
        UpdatedBy = userId;
        UpdatedAt = now;
        Version++;
        return Result.Success();
    }
}

/// <summary>
/// La règle des devis à relancer, sans IA : un devis envoyé, sans réponse et encore valable, dont la fin de validité
/// tombe dans la fenêtre réglée (aujourd'hui compris), et qui n'a pas déjà été relancé depuis qu'il y est entré.
/// </summary>
public static class QuoteFollowUpPolicy
{
    /// <summary>Jours avant la fin de validité (0 le dernier jour) ; <c>null</c> sans fin de validité.</summary>
    public static int? DaysLeft(DateOnly? validUntil, DateOnly today) => validUntil is { } until ? until.DayNumber - today.DayNumber : null;

    /// <summary>La fin de validité tombe dans la fenêtre : entre aujourd'hui et <paramref name="windowDays"/> jours.</summary>
    public static bool IsInWindow(DateOnly? validUntil, DateOnly today, int windowDays) =>
        DaysLeft(validUntil, today) is int left && left >= 0 && left <= windowDays;

    /// <summary>
    /// Le devis est à relancer : dans la fenêtre, et sans relance faite depuis le premier jour de la fenêtre
    /// (<paramref name="lastFollowUpOn"/>, la dernière relance partie ou notée, pas un envoi en échec).
    /// </summary>
    public static bool IsDue(QuoteStatus status, DateOnly? validUntil, DateOnly today, int windowDays, DateOnly? lastFollowUpOn) =>
        status == QuoteStatus.Validated
        && IsInWindow(validUntil, today, windowDays)
        && !(lastFollowUpOn is { } last && last >= validUntil!.Value.AddDays(-windowDays));
}

/// <summary>Codes d'erreur des devis à relancer.</summary>
public static class QuoteFollowUpErrors
{
    public const string WindowInvalidCode = "QuoteFollowUps.WindowInvalid";
    public const string QuoteNotPendingCode = "QuoteFollowUps.QuoteNotPending";
    public const string CustomerEmailMissingCode = "QuoteFollowUps.CustomerEmailMissing";
    public const string CompanyProfileMissingCode = "QuoteFollowUps.CompanyProfileMissing";
    public const string MessageInvalidCode = "QuoteFollowUps.MessageInvalid";

    public static Error WindowInvalid => Error.Validation(WindowInvalidCode, "WindowDays");

    public static Error QuoteNotPending => Error.Conflict(QuoteNotPendingCode);

    public static Error CustomerEmailMissing => Error.Conflict(CustomerEmailMissingCode);

    public static Error CompanyProfileMissing => Error.Conflict(CompanyProfileMissingCode);

    public static Error MessageInvalid => Error.Validation(MessageInvalidCode);
}
