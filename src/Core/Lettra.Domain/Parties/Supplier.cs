using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Parties;

/// <summary>
/// Fournisseur du dossier (émetteur des factures d'achat, bénéficiaire des règlements).
/// Invariants :
/// <list type="bullet">
/// <item>un nom (raison sociale) de 1 à 200 caractères ;</item>
/// <item>un fournisseur français a un SIREN : c'est par lui que les factures électroniques reçues
/// (réforme 2026-2027) seront rapprochées du fournisseur ;</item>
/// <item>un SIRET, s'il est donné, appartient au SIREN ; un numéro de TVA français aussi ;</item>
/// <item>un IBAN, s'il est donné, a une clé valide ;</item>
/// <item>le titulaire du compte, s'il est donné, tient en 70 caractères (limite des virements SEPA) ;</item>
/// <item>la dernière vérification du bénéficiaire vaut pour l'IBAN et le nom vérifiés : changer l'un ou l'autre l'efface.</item>
/// </list>
/// </summary>
public sealed class Supplier : AggregateRoot<SupplierId>, IVersioned
{
    public const int MaxNameLength = 200;

    /// <summary>Nom du bénéficiaire d'un virement SEPA : 70 caractères au plus.</summary>
    public const int MaxAccountHolderLength = 70;

    // Constructeur réservé à la persistance (EF), repris par le constructeur métier.
    private Supplier(SupplierId id)
        : base(id)
    {
        Name = string.Empty;
        Address = null!;
    }

    private Supplier(SupplierId id, Values values, DateTimeOffset createdAt)
        : this(id)
    {
        Name = values.Name;
        Siren = values.Ids.Siren;
        Siret = values.Ids.Siret;
        VatNumber = values.Ids.VatNumber;
        Email = values.Email;
        Iban = values.Iban;
        AccountHolderName = values.AccountHolderName;
        Address = values.Address;
        CreatedAt = createdAt;
    }

    public string Name { get; private set; }

    public Siren? Siren { get; private set; }

    public Siret? Siret { get; private set; }

    public VatNumber? VatNumber { get; private set; }

    public Email? Email { get; private set; }

    public Iban? Iban { get; private set; }

    /// <summary>Nom exact du titulaire du compte, s'il diffère du nom du fournisseur (nom commercial, gérant…).</summary>
    public string? AccountHolderName { get; private set; }

    /// <summary>Le nom à présenter à la banque pour un virement : le titulaire s'il est donné, sinon le nom du fournisseur.</summary>
    public string PayeeName => AccountHolderName ?? Name;

    /// <summary>Réponse de la dernière vérification du bénéficiaire, pour l'IBAN et le nom actuels.</summary>
    public PayeeMatch? PayeeCheckResult { get; private set; }

    public DateTimeOffset? PayeeCheckedAt { get; private set; }

    /// <summary>Le nom du titulaire que la banque propose quand le nom « correspond presque ».</summary>
    public string? PayeeCheckSuggestedName { get; private set; }

    public PostalAddress Address { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Supprimé par l'utilisateur : absent des listes, mais gardé en base comme les clients (voir <see cref="Customer.IsArchived"/>).</summary>
    public bool IsArchived { get; private set; }

    /// <summary>Version de concurrence optimiste (ADR-0013), avancée par la persistance à chaque écriture.</summary>
    public long Version { get; private set; }

    /// <summary>Données brutes de création ou de modification, telles que saisies ou reprises de l'annuaire SIRENE.</summary>
    public sealed record Draft(
        string? Name,
        string? Siren,
        string? Siret,
        string? VatNumber,
        string? Email,
        string? Iban,
        string? AddressLine1,
        string? AddressLine2,
        string? PostalCode,
        string? City,
        string? CountryCode,
        string? AccountHolderName = null);

    /// <summary>Crée un fournisseur en accumulant toutes les erreurs de saisie.</summary>
    public static Result<Supplier> Create(SupplierId id, Draft draft, DateTimeOffset now)
    {
        Result<Values> values = Read(draft);
        if (values.IsFailure)
        {
            return Result<Supplier>.Failure(values.Errors);
        }

        Values v = values.Value;
        Supplier supplier = new(id, v, now);
        supplier.Raise(new SupplierCreated(id, now));
        return supplier;
    }

    /// <summary>Remplace les données du fournisseur, avec les mêmes règles qu'à la création. Rien n'est modifié si une erreur est trouvée.</summary>
    public Result Update(Draft draft, DateTimeOffset now)
    {
        Result<Values> values = Read(draft);
        if (values.IsFailure)
        {
            return Result.Failure(values.Errors);
        }

        Values v = values.Value;
        string payeeBefore = PayeeName;
        Iban? ibanBefore = Iban;
        Name = v.Name;
        Siren = v.Ids.Siren;
        Siret = v.Ids.Siret;
        VatNumber = v.Ids.VatNumber;
        Email = v.Email;
        Iban = v.Iban;
        AccountHolderName = v.AccountHolderName;
        Address = v.Address;
        if (ibanBefore != Iban || !string.Equals(payeeBefore, PayeeName, StringComparison.Ordinal))
        {
            (PayeeCheckResult, PayeeCheckedAt, PayeeCheckSuggestedName) = (null, null, null);
        }

        Raise(new SupplierUpdated(Id, now));
        return Result.Success();
    }

    /// <summary>Retire le fournisseur des listes, ou l'y remet. Sans effet (et sans événement) si l'état est déjà celui demandé.</summary>
    public void SetArchived(bool archived, DateTimeOffset now)
    {
        if (IsArchived == archived)
        {
            return;
        }

        IsArchived = archived;
        Raise(new SupplierArchiveChanged(Id, archived, now));
    }

    /// <summary>
    /// Garde la réponse de la vérification du bénéficiaire pour l'IBAN et le nom actuels. Le nom proposé par la banque
    /// n'a de sens que si le nom « correspond presque » : il est ignoré sinon.
    /// </summary>
    public Result RecordPayeeCheck(PayeeMatch result, string? suggestedName, DateTimeOffset at)
    {
        if (Iban is null)
        {
            return SupplierErrors.IbanMissing;
        }

        if (!Enum.IsDefined(result))
        {
            throw new ArgumentOutOfRangeException(nameof(result), result, null);
        }

        string? suggested = result == PayeeMatch.CloseMatch && !string.IsNullOrWhiteSpace(suggestedName) ? suggestedName.Trim() : null;
        (PayeeCheckResult, PayeeCheckedAt, PayeeCheckSuggestedName) =
            (result, at, suggested is { Length: > MaxAccountHolderLength } ? suggested[..MaxAccountHolderLength] : suggested);
        return Result.Success();
    }

    private static Result<Values> Read(Draft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        List<Error> errors = [];

        string name = (draft.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            errors.Add(SupplierErrors.NameRequired);
        }
        else if (name.Length > MaxNameLength)
        {
            errors.Add(SupplierErrors.NameTooLong);
        }

        PartyIdentification ids = PartyIdentification.Read(draft.Siren, draft.Siret, draft.VatNumber, SupplierErrors.SiretSirenMismatch, errors);
        Email? email = PartyIdentification.Optional(draft.Email, SharedKernel.Email.Create, nameof(Email), errors);
        Iban? iban = PartyIdentification.Optional(draft.Iban, SharedKernel.Iban.Create, nameof(Iban), errors);
        string? holder = string.IsNullOrWhiteSpace(draft.AccountHolderName) ? null : draft.AccountHolderName.Trim();
        if (holder is { Length: > MaxAccountHolderLength })
        {
            errors.Add(SupplierErrors.AccountHolderTooLong);
        }

        Result<PostalAddress> address = PostalAddress.Create(
            draft.AddressLine1, draft.AddressLine2, draft.PostalCode, draft.City, draft.CountryCode);
        errors.AddRange(address.Errors);

        Result<CountryCode> country = CountryCode.Create(string.IsNullOrWhiteSpace(draft.CountryCode) ? CountryCode.France.Value : draft.CountryCode);
        if (country.IsSuccess && country.Value.IsFrance && ids.Siren is null && !PartyIdentification.HasIdentificationError(errors))
        {
            errors.Add(SupplierErrors.SirenRequired);
        }

        return errors.Count > 0
            ? Result<Values>.Failure(errors)
            : new Values(name, ids, email, iban, holder, address.Value);
    }

    private sealed record Values(string Name, PartyIdentification Ids, Email? Email, Iban? Iban, string? AccountHolderName, PostalAddress Address);
}
