using Lettra.Domain.Common;

namespace Lettra.Domain.Review;

/// <summary>
/// La liste de contrôle de la revue d'un dossier (mission de présentation des comptes, traçabilité qualité du cabinet).
/// L'ordre est celui de l'écran ; une clé n'est jamais renommée, elle est stockée avec chaque coche.
/// </summary>
public static class DossierReviewChecklist
{
    public const string DocumentsIssued = "pieces.emises";
    public const string PlatformFollowed = "pa.suivie";
    public const string ReceivablesFollowed = "relances.faites";
    public const string BankReconciled = "banque.rapprochee";
    public const string VatDeclared = "tva.declaree";
    public const string MissingDocumentsRequested = "pieces.manquantes";
    public const string PeriodClosed = "periode.cloturee";
    public const string NotesForManager = "points.manager";

    public static IReadOnlyList<string> Items { get; } =
    [
        DocumentsIssued, PlatformFollowed, ReceivablesFollowed, BankReconciled, VatDeclared, MissingDocumentsRequested,
        PeriodClosed, NotesForManager,
    ];

    public static bool Contains(string? key) => key is not null && Items.Contains(key, StringComparer.Ordinal);
}

/// <summary>Une coche de la revue : qui, quand, et une note facultative. Décocher la supprime.</summary>
public sealed class DossierReviewMark
{
    public const int MaxNoteLength = 500;

    private DossierReviewMark(string period, string itemKey, Guid checkedBy, DateTimeOffset checkedAt, string? note)
    {
        Period = period;
        ItemKey = itemKey;
        CheckedBy = checkedBy;
        CheckedAt = checkedAt;
        Note = note;
    }

    public string Period { get; private set; }

    public string ItemKey { get; private set; }

    public Guid CheckedBy { get; private set; }

    public DateTimeOffset CheckedAt { get; private set; }

    public string? Note { get; private set; }

    public static Result<DossierReviewMark> Check(ReviewPeriod period, string itemKey, Guid user, DateTimeOffset at, string? note)
    {
        if (!DossierReviewChecklist.Contains(itemKey))
        {
            return ReviewErrors.ItemUnknown;
        }

        string? trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        return trimmed?.Length > MaxNoteLength
            ? ReviewErrors.NoteTooLong
            : new DossierReviewMark(period.ToString(), itemKey, user, at, trimmed);
    }
}

public static class ReviewErrors
{
    public const string PeriodInvalidCode = "Review.PeriodInvalid";
    public const string ItemUnknownCode = "Review.ItemUnknown";
    public const string NoteTooLongCode = "Review.NoteTooLong";

    public static Error PeriodInvalid => Error.Validation(PeriodInvalidCode, "Period");

    public static Error ItemUnknown => Error.NotFound(ItemUnknownCode);

    public static Error NoteTooLong => Error.Validation(NoteTooLongCode, "Note");
}
