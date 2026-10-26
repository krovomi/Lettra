using Lettra.Domain.Common;
using Lettra.Domain.Parties;

namespace Lettra.Domain.Vigilance;

/// <summary>Où en est l'attestation de vigilance d'un fournisseur à une date donnée.</summary>
public enum AttestationStatus
{
    /// <summary>Contrat de moins de 5 000 € HT : rien n'est exigé.</summary>
    NotRequired = 0,

    /// <summary>Contrat d'au moins 5 000 € HT et aucune attestation reçue.</summary>
    Missing = 1,

    /// <summary>L'attestation a plus de six mois : en demander une nouvelle avant de payer.</summary>
    Expired = 2,

    /// <summary>Reçue, mais son code n'a pas été vérifié sur le site de l'URSSAF.</summary>
    NotVerified = 3,

    /// <summary>Valable, et elle expire dans les trente jours.</summary>
    ExpiringSoon = 4,

    Valid = 5,
}

/// <summary>
/// Le suivi de l'attestation de vigilance URSSAF d'un fournisseur (sous-traitant ou prestataire). Le donneur d'ordre doit
/// l'obtenir à la conclusion d'un contrat d'au moins 5 000 € HT, puis tous les six mois jusqu'à la fin de son exécution
/// (Code du travail, art. L8222-1 et D8222-5) ; sans elle, il est solidairement tenu des impôts, cotisations et salaires
/// si le sous-traitant est condamné pour travail dissimulé (art. L8222-2). Une ligne par fournisseur suivi.
/// </summary>
public sealed class SubcontractorAttestation : AggregateRoot<SupplierId>, IVersioned
{
    /// <summary>Seuil du contrat à partir duquel l'attestation est exigée (art. R8222-1).</summary>
    public const decimal Threshold = 5_000m;

    public const decimal MaxContractAmount = 100_000_000m;

    /// <summary>Durée de validité d'une attestation : six mois à compter de sa date.</summary>
    public const int ValidityMonths = 6;

    /// <summary>Une attestation qui expire dans ce délai s'annonce.</summary>
    public const int NoticeDays = 30;

    public const int MinCodeLength = 8;
    public const int MaxCodeLength = 20;

    // Constructeur réservé à la persistance.
    private SubcontractorAttestation(SupplierId id)
        : base(id)
    {
    }

    /// <summary>Le montant HT du contrat en cours avec ce fournisseur (ou des prestations prévues sur l'année).</summary>
    public decimal ContractAmountExclTax { get; private set; }

    /// <summary>La date de l'attestation reçue ; vide tant qu'aucune n'est reçue.</summary>
    public DateOnly? IssuedOn { get; private set; }

    /// <summary>Le code de sécurité imprimé sur l'attestation, à vérifier sur le site de l'URSSAF.</summary>
    public string? SecurityCode { get; private set; }

    /// <summary>Le jour où le cabinet a vérifié le code sur le site de l'URSSAF.</summary>
    public DateOnly? VerifiedOn { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public long Version { get; private set; }

    public bool IsRequired => ContractAmountExclTax >= Threshold;

    /// <summary>Le dernier jour de validité de l'attestation (six mois après sa date, la veille du même jour).</summary>
    public DateOnly? ValidUntil => IssuedOn?.AddMonths(ValidityMonths).AddDays(-1);

    public static Result<SubcontractorAttestation> Create(SupplierId supplier, Draft draft, DateOnly today, DateTimeOffset now)
    {
        SubcontractorAttestation attestation = new(supplier);
        Result applied = attestation.Update(draft, today, now);
        return applied.IsFailure ? Result<SubcontractorAttestation>.Failure(applied.Errors) : attestation;
    }

    public Result Update(Draft draft, DateOnly today, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        string? code = string.IsNullOrWhiteSpace(draft.SecurityCode) ? null : draft.SecurityCode.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        List<Error> errors = [.. Validate(draft, code, today)];
        if (errors.Count > 0)
        {
            return Result.Failure(errors);
        }

        ContractAmountExclTax = draft.ContractAmountExclTax;
        IssuedOn = draft.IssuedOn;
        SecurityCode = code;
        VerifiedOn = draft.VerifiedOn;
        UpdatedAt = now;
        return Result.Success();
    }

    public AttestationStatus StatusOn(DateOnly today)
    {
        if (!IsRequired)
        {
            return AttestationStatus.NotRequired;
        }

        if (ValidUntil is not { } until)
        {
            return AttestationStatus.Missing;
        }

        if (until < today)
        {
            return AttestationStatus.Expired;
        }

        if (VerifiedOn is null)
        {
            return AttestationStatus.NotVerified;
        }

        return until.DayNumber - today.DayNumber < NoticeDays ? AttestationStatus.ExpiringSoon : AttestationStatus.Valid;
    }

    /// <summary>Payer ce fournisseur expose le cabinet : attestation exigée mais absente, périmée ou non vérifiée.</summary>
    public static bool BlocksPayment(AttestationStatus status) =>
        status is AttestationStatus.Missing or AttestationStatus.Expired or AttestationStatus.NotVerified;

    private static IEnumerable<Error> Validate(Draft draft, string? code, DateOnly today)
    {
        if (draft.ContractAmountExclTax is < 0m or > MaxContractAmount || decimal.Round(draft.ContractAmountExclTax, 2) != draft.ContractAmountExclTax)
        {
            yield return AttestationErrors.AmountInvalid;
        }

        if (draft.IssuedOn > today || draft.VerifiedOn > today)
        {
            yield return AttestationErrors.DateInFuture;
        }

        if (code is not null && (code.Length is < MinCodeLength or > MaxCodeLength || !code.All(char.IsAsciiLetterOrDigit)))
        {
            yield return AttestationErrors.CodeInvalid;
        }

        if (draft.VerifiedOn is not null && (draft.IssuedOn is null || code is null))
        {
            yield return AttestationErrors.VerificationNeedsCode;
        }
        else if (draft.VerifiedOn < draft.IssuedOn)
        {
            yield return AttestationErrors.VerifiedBeforeIssued;
        }
    }

    /// <summary>Ce que l'écran envoie pour un fournisseur.</summary>
    public sealed record Draft(decimal ContractAmountExclTax, DateOnly? IssuedOn, string? SecurityCode, DateOnly? VerifiedOn);
}

public static class AttestationErrors
{
    public const string SupplierNotFoundCode = "Attestation.SupplierNotFound";
    public const string AmountInvalidCode = "Attestation.AmountInvalid";
    public const string DateInFutureCode = "Attestation.DateInFuture";
    public const string CodeInvalidCode = "Attestation.CodeInvalid";
    public const string VerificationNeedsCodeCode = "Attestation.VerificationNeedsCode";
    public const string VerifiedBeforeIssuedCode = "Attestation.VerifiedBeforeIssued";

    public static Error SupplierNotFound => Error.NotFound(SupplierNotFoundCode);

    public static Error AmountInvalid => Error.Validation(AmountInvalidCode, "ContractAmountExclTax");

    public static Error DateInFuture => Error.Validation(DateInFutureCode, "IssuedOn");

    public static Error CodeInvalid => Error.Validation(CodeInvalidCode, "SecurityCode");

    public static Error VerificationNeedsCode => Error.Validation(VerificationNeedsCodeCode, "VerifiedOn");

    public static Error VerifiedBeforeIssued => Error.Validation(VerifiedBeforeIssuedCode, "VerifiedOn");
}
