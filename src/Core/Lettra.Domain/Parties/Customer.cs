using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Parties;

/// <summary>
/// Client du dossier (destinataire des devis et factures).
/// Invariants :
/// <list type="bullet">
/// <item>un nom (raison sociale ou nom du particulier) de 1 à 200 caractères ;</item>
/// <item>une entreprise française a un SIREN (mention obligatoire de la réforme 2026-2027) ;</item>
/// <item>un SIRET, s'il est donné, appartient au SIREN ;</item>
/// <item>un particulier n'a ni SIREN, ni SIRET, ni numéro de TVA.</item>
/// </list>
/// </summary>
public sealed class Customer : AggregateRoot<CustomerId>
{
    public const int MaxNameLength = 200;

    // Constructeur réservé à la persistance.
    private Customer(CustomerId id)
        : base(id)
    {
        Name = string.Empty;
        BillingAddress = null!;
    }

    private Customer(
        CustomerId id,
        CustomerKind kind,
        string name,
        Siren? siren,
        Siret? siret,
        VatNumber? vatNumber,
        Email? email,
        PostalAddress billingAddress,
        DateTimeOffset createdAt)
        : base(id)
    {
        Kind = kind;
        Name = name;
        Siren = siren;
        Siret = siret;
        VatNumber = vatNumber;
        Email = email;
        BillingAddress = billingAddress;
        CreatedAt = createdAt;
    }

    public CustomerKind Kind { get; private set; }

    public string Name { get; private set; }

    public Siren? Siren { get; private set; }

    public Siret? Siret { get; private set; }

    public VatNumber? VatNumber { get; private set; }

    public Email? Email { get; private set; }

    public PostalAddress BillingAddress { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Données brutes de création, telles que saisies. Les objets-valeurs sont construits et validés ici.</summary>
    public sealed record Draft(
        CustomerKind Kind,
        string? Name,
        string? Siren,
        string? Siret,
        string? VatNumber,
        string? Email,
        string? AddressLine1,
        string? AddressLine2,
        string? PostalCode,
        string? City,
        string? CountryCode);

    /// <summary>Crée un client en accumulant <b>toutes</b> les erreurs de saisie (l'écran les affiche ensemble).</summary>
    public static Result<Customer> Create(CustomerId id, Draft draft, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        List<Error> errors = [];

        if (!Enum.IsDefined(draft.Kind))
        {
            errors.Add(CustomerErrors.KindInvalid);
        }

        string name = (draft.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            errors.Add(CustomerErrors.NameRequired);
        }
        else if (name.Length > MaxNameLength)
        {
            errors.Add(CustomerErrors.NameTooLong);
        }

        Siren? siren = Optional(draft.Siren, SharedKernel.Siren.Create, nameof(Siren), errors);
        Siret? siret = Optional(draft.Siret, SharedKernel.Siret.Create, nameof(Siret), errors);
        VatNumber? vat = Optional(draft.VatNumber, SharedKernel.VatNumber.Create, nameof(VatNumber), errors);
        Email? email = Optional(draft.Email, SharedKernel.Email.Create, nameof(Email), errors);

        Result<PostalAddress> address = PostalAddress.Create(
            draft.AddressLine1, draft.AddressLine2, draft.PostalCode, draft.City, draft.CountryCode);
        errors.AddRange(address.Errors);

        // Le SIRET porte le SIREN : on le déduit, ou on vérifie la cohérence.
        if (siret is not null)
        {
            if (siren is null)
            {
                siren = siret.Siren;
            }
            else if (siren != siret.Siren)
            {
                errors.Add(CustomerErrors.SiretSirenMismatch);
            }
        }

        if (draft.Kind == CustomerKind.Individual && (siren is not null || vat is not null))
        {
            errors.Add(CustomerErrors.IndividualCannotHaveCompanyIds);
        }

        // Le pays se lit indépendamment du reste de l'adresse : un code postal faux ne doit pas masquer l'absence de SIREN.
        Result<CountryCode> country = CountryCode.Create(string.IsNullOrWhiteSpace(draft.CountryCode) ? CountryCode.France.Value : draft.CountryCode);
        bool isFrench = country.IsSuccess && country.Value.IsFrance;
        if (draft.Kind == CustomerKind.Company && isFrench && siren is null && !HasError(errors, nameof(Siren), nameof(Siret)))
        {
            errors.Add(CustomerErrors.SirenRequired);
        }

        if (errors.Count > 0)
        {
            return Result<Customer>.Failure(errors);
        }

        Customer customer = new(id, draft.Kind, name, siren, siret, vat, email, address.Value, now);
        customer.Raise(new CustomerCreated(id, now));
        return customer;
    }

    private static T? Optional<T>(string? raw, Func<string?, Result<T>> factory, string target, List<Error> errors)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        Result<T> result = factory(raw);
        if (result.IsSuccess)
        {
            return result.Value;
        }

        errors.AddRange(result.Errors.Select(e => e.For(target)));
        return null;
    }

    private static bool HasError(List<Error> errors, params string[] targets) =>
        errors.Exists(e => e.Target is not null && targets.Contains(e.Target));
}
