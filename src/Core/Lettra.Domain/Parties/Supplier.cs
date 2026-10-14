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
/// <item>un IBAN, s'il est donné, a une clé valide.</item>
/// </list>
/// </summary>
public sealed class Supplier : AggregateRoot<SupplierId>
{
    public const int MaxNameLength = 200;

    // Constructeur réservé à la persistance.
    private Supplier(SupplierId id)
        : base(id)
    {
        Name = string.Empty;
        Address = null!;
    }

    private Supplier(SupplierId id, string name, PartyIdentification ids, Email? email, Iban? iban, PostalAddress address, DateTimeOffset createdAt)
        : base(id)
    {
        Name = name;
        Siren = ids.Siren;
        Siret = ids.Siret;
        VatNumber = ids.VatNumber;
        Email = email;
        Iban = iban;
        Address = address;
        CreatedAt = createdAt;
    }

    public string Name { get; private set; }

    public Siren? Siren { get; private set; }

    public Siret? Siret { get; private set; }

    public VatNumber? VatNumber { get; private set; }

    public Email? Email { get; private set; }

    public Iban? Iban { get; private set; }

    public PostalAddress Address { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Données brutes de création, telles que saisies ou reprises de l'annuaire SIRENE.</summary>
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
        string? CountryCode);

    /// <summary>Crée un fournisseur en accumulant toutes les erreurs de saisie.</summary>
    public static Result<Supplier> Create(SupplierId id, Draft draft, DateTimeOffset now)
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

        Result<PostalAddress> address = PostalAddress.Create(
            draft.AddressLine1, draft.AddressLine2, draft.PostalCode, draft.City, draft.CountryCode);
        errors.AddRange(address.Errors);

        Result<CountryCode> country = CountryCode.Create(string.IsNullOrWhiteSpace(draft.CountryCode) ? CountryCode.France.Value : draft.CountryCode);
        if (country.IsSuccess && country.Value.IsFrance && ids.Siren is null && !PartyIdentification.HasIdentificationError(errors))
        {
            errors.Add(SupplierErrors.SirenRequired);
        }

        if (errors.Count > 0)
        {
            return Result<Supplier>.Failure(errors);
        }

        Supplier supplier = new(id, name, ids, email, iban, address.Value, now);
        supplier.Raise(new SupplierCreated(id, now));
        return supplier;
    }
}
