using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Parties;

public readonly record struct IbanChangeId(Guid Value)
{
    public static IbanChangeId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Où en est la vérification d'un changement d'IBAN.</summary>
public enum IbanChangeStatus
{
    /// <summary>À vérifier : aucun paiement vers le nouvel IBAN tant qu'il n'est pas confirmé.</summary>
    Pending = 1,

    /// <summary>Confirmé auprès du fournisseur par un autre canal que la demande.</summary>
    Confirmed = 2,

    /// <summary>Refusé : la demande est suspecte (tentative de fraude au faux fournisseur).</summary>
    Rejected = 3,

    /// <summary>Remplacé par un changement plus récent, qui seul reste à vérifier.</summary>
    Superseded = 4,
}

/// <summary>Le canal par lequel le changement a été vérifié, distinct de celui de la demande.</summary>
public enum IbanVerificationMethod
{
    /// <summary>Appel au numéro de téléphone habituel du fournisseur (pas celui de la demande).</summary>
    KnownPhoneNumber = 1,

    /// <summary>Courrier signé ou attestation bancaire (RIB) reçue par un canal connu.</summary>
    SignedLetter = 2,

    /// <summary>Rencontre en personne.</summary>
    InPerson = 3,

    /// <summary>Autre canal, décrit dans la note.</summary>
    Other = 4,
}

/// <summary>
/// Un changement d'IBAN d'un fournisseur, à vérifier avant tout paiement : la parade contre la fraude au faux
/// fournisseur, où un escroc demande par e-mail de payer sur un nouveau compte. Seules les formes masquées des IBAN sont
/// gardées (pays, clé et quatre derniers caractères) : l'IBAN complet reste sur la fiche du fournisseur.
/// Invariants :
/// <list type="bullet">
/// <item>seul un changement à vérifier peut être confirmé, refusé ou remplacé ;</item>
/// <item>une confirmation dit par quel canal ; une vérification « autre » et un refus portent une note ;</item>
/// <item>la note fait au plus <see cref="MaxNoteLength"/> caractères.</item>
/// </list>
/// </summary>
public sealed class IbanChange : AggregateRoot<IbanChangeId>, IVersioned
{
    public const int MaxNoteLength = 500;

    // Constructeur réservé à la persistance (EF), repris par le constructeur métier.
    private IbanChange(IbanChangeId id)
        : base(id)
    {
        NewIban = string.Empty;
    }

    private IbanChange(IbanChangeId id, SupplierId supplierId, string? previousIban, string newIban, bool countryChanged, Guid changedBy, DateTimeOffset changedAt)
        : this(id)
    {
        SupplierId = supplierId;
        PreviousIban = previousIban;
        NewIban = newIban;
        CountryChanged = countryChanged;
        ChangedBy = changedBy;
        ChangedAt = changedAt;
        Status = IbanChangeStatus.Pending;
    }

    public SupplierId SupplierId { get; private set; }

    /// <summary>L'IBAN remplacé, masqué ; absent si le fournisseur n'en avait pas.</summary>
    public string? PreviousIban { get; private set; }

    /// <summary>Le nouvel IBAN, masqué.</summary>
    public string NewIban { get; private set; }

    /// <summary>Le compte change de pays : signal fort de fraude.</summary>
    public bool CountryChanged { get; private set; }

    public Guid ChangedBy { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public IbanChangeStatus Status { get; private set; }

    public Guid? DecidedBy { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public IbanVerificationMethod? Method { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Version de concurrence optimiste (ADR-0013), avancée par la persistance à chaque écriture.</summary>
    public long Version { get; private set; }

    public bool IsPending => Status == IbanChangeStatus.Pending;

    /// <summary>
    /// Un changement à vérifier, si l'IBAN change vraiment : un nouvel IBAN (ou un premier IBAN ajouté après la création),
    /// pas une suppression ni une simple réécriture du même compte.
    /// </summary>
    public static IbanChange? Detect(SupplierId supplierId, Iban? previous, Iban? next, Guid changedBy, DateTimeOffset now)
    {
        if (next is null || next == previous)
        {
            return null;
        }

        bool countryChanged = previous is not null && !previous.Value.AsSpan(0, 2).SequenceEqual(next.Value.AsSpan(0, 2));
        return new IbanChange(IbanChangeId.New(), supplierId, previous is null ? null : Mask(previous), Mask(next), countryChanged, changedBy, now);
    }

    /// <summary>Forme masquée : pays et clé, puis les quatre derniers caractères (FR76 •••• 0189).</summary>
    public static string Mask(Iban iban)
    {
        ArgumentNullException.ThrowIfNull(iban);
        return $"{iban.Value[..4]} •••• {iban.Value[^4..]}";
    }

    public Result Confirm(IbanVerificationMethod method, string? note, Guid by, DateTimeOffset now)
    {
        string? text = Normalize(note);
        if (!Enum.IsDefined(method))
        {
            return IbanChangeErrors.MethodRequired;
        }

        if (method == IbanVerificationMethod.Other && text is null)
        {
            return IbanChangeErrors.NoteRequired;
        }

        return Decide(IbanChangeStatus.Confirmed, method, text, by, now);
    }

    public Result Reject(string? note, Guid by, DateTimeOffset now)
    {
        string? text = Normalize(note);
        return text is null ? IbanChangeErrors.NoteRequired : Decide(IbanChangeStatus.Rejected, null, text, by, now);
    }

    /// <summary>Un changement plus récent du même fournisseur prend la place de celui-ci, encore à vérifier.</summary>
    public void Supersede(DateTimeOffset now)
    {
        if (IsPending)
        {
            Status = IbanChangeStatus.Superseded;
            DecidedAt = now;
        }
    }

    private Result Decide(IbanChangeStatus status, IbanVerificationMethod? method, string? note, Guid by, DateTimeOffset now)
    {
        if (!IsPending)
        {
            return IbanChangeErrors.AlreadyDecided;
        }

        if (note is { Length: > MaxNoteLength })
        {
            return IbanChangeErrors.NoteTooLong;
        }

        Status = status;
        Method = method;
        Note = note;
        DecidedBy = by;
        DecidedAt = now;
        return Result.Success();
    }

    private static string? Normalize(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}

/// <summary>Codes d'erreur des changements d'IBAN.</summary>
public static class IbanChangeErrors
{
    public const string NotFoundCode = "IbanChange.NotFound";
    public const string AlreadyDecidedCode = "IbanChange.AlreadyDecided";
    public const string MethodRequiredCode = "IbanChange.MethodRequired";
    public const string NoteRequiredCode = "IbanChange.NoteRequired";
    public const string NoteTooLongCode = "IbanChange.NoteTooLong";
    public const string SameUserCode = "IbanChange.SameUser";

    public static Error NotFound => Error.NotFound(NotFoundCode);

    public static Error AlreadyDecided => Error.Conflict(AlreadyDecidedCode);

    public static Error MethodRequired => Error.Validation(MethodRequiredCode, nameof(IbanChange.Method));

    public static Error NoteRequired => Error.Validation(NoteRequiredCode, nameof(IbanChange.Note));

    public static Error NoteTooLong => Error.Validation(NoteTooLongCode, nameof(IbanChange.Note));

    /// <summary>Séparation des tâches : celui qui a saisi le nouvel IBAN ne le vérifie pas lui-même.</summary>
    public static Error SameUser => new(SameUserCode, ErrorKind.Forbidden);
}
