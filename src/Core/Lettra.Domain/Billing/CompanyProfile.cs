using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Billing;

public static class CompanyProfileErrors
{
    public const string NameRequiredCode = "CompanyProfile.NameRequired";
    public const string NameTooLongCode = "CompanyProfile.NameTooLong";
    public const string PaymentDaysInvalidCode = "CompanyProfile.PaymentDaysInvalid";
    public const string NotFoundCode = "CompanyProfile.NotFound";

    public static Error NameRequired => Error.Validation(NameRequiredCode, nameof(CompanyProfile.Name));
    public static Error NameTooLong => Error.Validation(NameTooLongCode, nameof(CompanyProfile.Name));
    public static Error PaymentDaysInvalid => Error.Validation(PaymentDaysInvalidCode, nameof(CompanyProfile.PaymentDays));
    public static Error NotFound => Error.NotFound(NotFoundCode);
    internal static Error TooLong(string target) => Error.Validation("Validation.TooLong", target);
    internal static Error Invalid(string target) => Error.Validation("Validation.Invalid", target);
}

/// <summary>
/// Fiche de l'entreprise du dossier, émettrice des devis et factures (une seule par base de dossier). Ses mentions sont
/// copiées sur chaque pièce à la validation. Invariants : une dénomination, une adresse valide, un SIRET qui
/// appartient au SIREN, un délai de paiement de 0 à 60 jours (art. L441-10 C.com).
/// </summary>
public sealed class CompanyProfile : AggregateRoot<Guid>, IVersioned
{
    /// <summary>Identifiant fixe : la fiche est unique dans la base du dossier.</summary>
    public static readonly Guid SingletonId = new("6c657474-7261-0000-0000-70726f66696c");

    public const int MaxNameLength = 200;
    public const int MaxPaymentDays = 60;

    // Constructeur réservé à la persistance.
    private CompanyProfile(Guid id)
        : base(id)
    {
        Name = string.Empty;
        Address = null!;
    }

    public string Name { get; private set; }

    public string? LegalForm { get; private set; }

    public decimal? ShareCapital { get; private set; }

    public Siren? Siren { get; private set; }

    public Siret? Siret { get; private set; }

    public string? Rcs { get; private set; }

    public VatNumber? VatNumber { get; private set; }

    public PostalAddress Address { get; private set; }

    public bool VatFranchise { get; private set; }

    public bool VatOnDebits { get; private set; }

    /// <summary>Régime de déclaration de la TVA ; toujours <c>null</c> en franchise en base, et tant qu'il n'est pas renseigné.</summary>
    public VatDeclarationRegime? VatDeclarationRegime { get; private set; }

    public int PaymentDays { get; private set; }

    public string? PaymentTerms { get; private set; }

    public decimal? LatePenaltyRate { get; private set; }

    public string? EarlyPaymentDiscount { get; private set; }

    public Iban? Iban { get; private set; }

    public string? Bic { get; private set; }

    public long Version { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Données saisies, telles quelles.</summary>
    public sealed record Draft(
        string? Name,
        string? LegalForm,
        decimal? ShareCapital,
        string? Siren,
        string? Siret,
        string? Rcs,
        string? VatNumber,
        string? AddressLine1,
        string? AddressLine2,
        string? PostalCode,
        string? City,
        string? CountryCode,
        bool VatFranchise,
        bool VatOnDebits,
        int PaymentDays,
        string? PaymentTerms,
        decimal? LatePenaltyRate,
        string? EarlyPaymentDiscount,
        string? Iban,
        string? Bic,
        VatDeclarationRegime? VatDeclarationRegime = null);

    public static Result<CompanyProfile> Create(Draft draft, DateTimeOffset now)
    {
        CompanyProfile profile = new(SingletonId);
        Result applied = profile.Apply(draft, now);
        return applied.IsSuccess ? profile : Result<CompanyProfile>.Failure(applied.Errors);
    }

    public Result Update(Draft draft, DateTimeOffset now) => Apply(draft, now);

    /// <summary>Copie de l'émetteur et de ses conditions, posée sur une pièce à sa validation.</summary>
    public SellerTerms ToSellerTerms() => new(
        new PartySnapshot(
            Name, Address.Line1, Address.Line2, Address.PostalCode, Address.City, Address.Country.Value,
            Siren?.Value, Siret?.Value, VatNumber?.Value, LegalForm, ShareCapital, Rcs, IsBusiness: true),
        VatFranchise, VatOnDebits, PaymentDays, PaymentTerms, LatePenaltyRate, EarlyPaymentDiscount);

    private Result Apply(Draft draft, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        List<Error> errors = [];

        string name = (draft.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            errors.Add(CompanyProfileErrors.NameRequired);
        }
        else if (name.Length > MaxNameLength)
        {
            errors.Add(CompanyProfileErrors.NameTooLong);
        }

        PartyIdentification ids = PartyIdentification.Read(draft.Siren, draft.Siret, draft.VatNumber, CustomerErrors.SiretSirenMismatch, errors);
        Result<PostalAddress> address = PostalAddress.Create(draft.AddressLine1, draft.AddressLine2, draft.PostalCode, draft.City, draft.CountryCode);
        errors.AddRange(address.Errors);
        Iban? iban = PartyIdentification.Optional(draft.Iban, SharedKernel.Iban.Create, nameof(Iban), errors);

        if (draft.PaymentDays is < 0 or > MaxPaymentDays)
        {
            errors.Add(CompanyProfileErrors.PaymentDaysInvalid);
        }

        if (draft.VatDeclarationRegime is { } regime && !Enum.IsDefined(regime))
        {
            errors.Add(CompanyProfileErrors.Invalid(nameof(VatDeclarationRegime)));
        }

        if (draft.ShareCapital is < 0m || draft.LatePenaltyRate is < 0m or > 100m)
        {
            errors.Add(CompanyProfileErrors.Invalid(draft.ShareCapital is < 0m ? nameof(ShareCapital) : nameof(LatePenaltyRate)));
        }

        string? legalForm = Clean(draft.LegalForm, 50, nameof(LegalForm), errors);
        string? rcs = Clean(draft.Rcs, 100, nameof(Rcs), errors);
        string? terms = Clean(draft.PaymentTerms, Invoice.MaxPaymentTermsLength, nameof(PaymentTerms), errors);
        string? discount = Clean(draft.EarlyPaymentDiscount, 200, nameof(EarlyPaymentDiscount), errors);
        string? bic = Clean(draft.Bic, 11, nameof(Bic), errors)?.ToUpperInvariant();

        if (errors.Count > 0)
        {
            return Result.Failure(errors);
        }

        Name = name;
        LegalForm = legalForm;
        ShareCapital = draft.ShareCapital;
        Siren = ids.Siren;
        Siret = ids.Siret;
        VatNumber = ids.VatNumber;
        Rcs = rcs;
        Address = address.Value;
        VatFranchise = draft.VatFranchise;
        VatOnDebits = draft.VatOnDebits;
        VatDeclarationRegime = draft.VatFranchise ? null : draft.VatDeclarationRegime;
        PaymentDays = draft.PaymentDays;
        PaymentTerms = terms;
        LatePenaltyRate = draft.LatePenaltyRate;
        EarlyPaymentDiscount = discount;
        Iban = iban;
        Bic = bic;
        UpdatedAt = now;
        Version++;
        return Result.Success();
    }

    private static string? Clean(string? value, int max, string target, List<Error> errors)
    {
        string? clean = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (clean is { Length: var length } && length > max)
        {
            errors.Add(CompanyProfileErrors.TooLong(target));
        }

        return clean;
    }
}
