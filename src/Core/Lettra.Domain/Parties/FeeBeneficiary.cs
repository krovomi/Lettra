using Lettra.Domain.Common;

namespace Lettra.Domain.Parties;

/// <summary>Ce que le dossier verse à un bénéficiaire, au sens de la déclaration des honoraires (DAS 2, art. 240 du CGI).</summary>
public enum FeeNature
{
    /// <summary>Honoraires et vacations.</summary>
    Fees = 1,

    Commissions = 2,

    /// <summary>Courtages.</summary>
    Brokerage = 3,

    /// <summary>Ristournes.</summary>
    Rebates = 4,

    /// <summary>Droits d'auteur.</summary>
    Copyright = 5,

    /// <summary>Droits d'inventeur.</summary>
    InventorRoyalties = 6,

    /// <summary>Autres rémunérations (jetons de présence, gratifications).</summary>
    Other = 7,
}

/// <summary>Une somme versée à un bénéficiaire hors des relevés bancaires du dossier (chèque, espèces, compensation), pour un exercice.</summary>
public sealed class FeePaidOutside
{
    // Constructeur réservé à la persistance.
    private FeePaidOutside()
    {
    }

    internal FeePaidOutside(DateOnly fiscalYearStart, decimal amount)
    {
        FiscalYearStart = fiscalYearStart;
        Amount = amount;
    }

    /// <summary>Le premier jour de l'exercice concerné.</summary>
    public DateOnly FiscalYearStart { get; private set; }

    /// <summary>Le montant versé, toutes taxes comprises.</summary>
    public decimal Amount { get; internal set; }
}

/// <summary>
/// Un fournisseur dont les versements entrent dans la déclaration des honoraires (DAS 2) : honoraires, commissions,
/// courtages, ristournes, droits d'auteur ou d'inventeur versés à un tiers non salarié. Ils se déclarent quand ils
/// dépassent 2 400 € TTC par an et par bénéficiaire (art. 240 du CGI ; 1 200 € avant les sommes versées en 2024), avec la déclaration de résultats. Les versements
/// se lisent sur les relevés bancaires ; ceux faits autrement se notent ici, par exercice.
/// </summary>
public sealed class FeeBeneficiary : AggregateRoot<SupplierId>, IVersioned
{
    /// <summary>Au-delà de ce total TTC versé dans l'année, le bénéficiaire se déclare (art. 240 du CGI ; BOI-BIC-DECLA-30-70-20).</summary>
    public const decimal DeclarationThreshold = 2_400m;

    /// <summary>Le seuil des exercices clos avant 2024, avant son relèvement à 2 400 €.</summary>
    public const decimal DeclarationThresholdBefore2024 = 1_200m;

    public const int MaxProfessionLength = 60;
    public const decimal MaxAmount = 100_000_000m;

    private readonly List<FeePaidOutside> _paidOutside = [];

    // Constructeur réservé à la persistance.
    private FeeBeneficiary(SupplierId id)
        : base(id)
    {
        Profession = string.Empty;
    }

    public FeeNature Nature { get; private set; }

    /// <summary>La profession du bénéficiaire, demandée par la déclaration (avocat, architecte, agent commercial…).</summary>
    public string Profession { get; private set; }

    public IReadOnlyList<FeePaidOutside> PaidOutside => _paidOutside;

    public DateTimeOffset UpdatedAt { get; private set; }

    public long Version { get; private set; }

    /// <summary>Le seuil de l'exercice : 2 400 € à partir des sommes versées en 2024, 1 200 € pour les exercices clos avant.</summary>
    public static decimal ThresholdFor(DateOnly fiscalYearEnd) =>
        fiscalYearEnd.Year >= 2024 ? DeclarationThreshold : DeclarationThresholdBefore2024;

    /// <summary>Le total versé dans l'exercice dépasse son seuil : le bénéficiaire figure sur la déclaration.</summary>
    public static bool MustBeDeclared(decimal totalPaid, DateOnly fiscalYearEnd) => totalPaid > ThresholdFor(fiscalYearEnd);

    public static Result<FeeBeneficiary> Create(SupplierId supplier, Draft draft, DateTimeOffset now)
    {
        FeeBeneficiary beneficiary = new(supplier);
        Result applied = beneficiary.Update(draft, now);
        return applied.IsFailure ? Result<FeeBeneficiary>.Failure(applied.Errors) : beneficiary;
    }

    public Result Update(Draft draft, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        string profession = draft.Profession?.Trim() ?? string.Empty;
        List<Error> errors = [];
        if (!Enum.IsDefined(draft.Nature))
        {
            errors.Add(FeeErrors.NatureInvalid);
        }

        if (profession.Length == 0)
        {
            errors.Add(FeeErrors.ProfessionRequired);
        }
        else if (profession.Length > MaxProfessionLength)
        {
            errors.Add(FeeErrors.ProfessionTooLong);
        }

        if (draft.PaidOutside is { } paid && (paid.Amount is < 0m or > MaxAmount || decimal.Round(paid.Amount, 2) != paid.Amount))
        {
            errors.Add(FeeErrors.AmountInvalid);
        }

        if (errors.Count > 0)
        {
            return Result.Failure(errors);
        }

        Nature = draft.Nature;
        Profession = profession;
        if (draft.PaidOutside is { } outside)
        {
            SetPaidOutside(outside.FiscalYearStart, outside.Amount);
        }

        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Ce qui a été versé hors relevés pendant l'exercice qui commence le <paramref name="fiscalYearStart"/>.</summary>
    public decimal PaidOutsideFor(DateOnly fiscalYearStart) =>
        _paidOutside.FirstOrDefault(p => p.FiscalYearStart == fiscalYearStart)?.Amount ?? 0m;

    private void SetPaidOutside(DateOnly fiscalYearStart, decimal amount)
    {
        FeePaidOutside? existing = _paidOutside.FirstOrDefault(p => p.FiscalYearStart == fiscalYearStart);
        if (amount == 0m)
        {
            if (existing is not null)
            {
                _paidOutside.Remove(existing);
            }
        }
        else if (existing is null)
        {
            _paidOutside.Add(new FeePaidOutside(fiscalYearStart, amount));
        }
        else
        {
            existing.Amount = amount;
        }
    }

    /// <summary>
    /// Ce que l'écran envoie : la nature et la profession, et le cas échéant ce qui a été versé hors relevés pendant un
    /// exercice (zéro retire la somme notée).
    /// </summary>
    public sealed record Draft(FeeNature Nature, string Profession, YearAmount? PaidOutside = null);

    /// <summary>Un montant pour l'exercice qui commence le <paramref name="FiscalYearStart"/>.</summary>
    public sealed record YearAmount(DateOnly FiscalYearStart, decimal Amount);
}

public static class FeeErrors
{
    public const string SupplierNotFoundCode = "Fee.SupplierNotFound";
    public const string BeneficiaryNotFoundCode = "Fee.BeneficiaryNotFound";
    public const string NatureInvalidCode = "Fee.NatureInvalid";
    public const string ProfessionRequiredCode = "Fee.ProfessionRequired";
    public const string ProfessionTooLongCode = "Fee.ProfessionTooLong";
    public const string AmountInvalidCode = "Fee.AmountInvalid";
    public const string YearInvalidCode = "Fee.YearInvalid";

    public static Error SupplierNotFound => Error.NotFound(SupplierNotFoundCode);

    public static Error BeneficiaryNotFound => Error.NotFound(BeneficiaryNotFoundCode);

    public static Error NatureInvalid => Error.Validation(NatureInvalidCode, "Nature");

    public static Error ProfessionRequired => Error.Validation(ProfessionRequiredCode, "Profession");

    public static Error ProfessionTooLong => Error.Validation(ProfessionTooLongCode, "Profession");

    public static Error AmountInvalid => Error.Validation(AmountInvalidCode, "PaidOutside");

    /// <summary>L'exercice demandé n'est pas un exercice du dossier (son premier jour ne suit pas une clôture).</summary>
    public static Error YearInvalid => Error.Validation(YearInvalidCode, "FiscalYearStart");
}
