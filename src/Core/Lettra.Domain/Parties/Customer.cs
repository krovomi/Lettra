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
public sealed class Customer : AggregateRoot<CustomerId>, IVersioned
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
        string? preferredLanguage,
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
        PreferredLanguage = preferredLanguage;
        CreatedAt = createdAt;
    }

    public CustomerKind Kind { get; private set; }

    public string Name { get; private set; }

    public Siren? Siren { get; private set; }

    public Siret? Siret { get; private set; }

    public VatNumber? VatNumber { get; private set; }

    public Email? Email { get; private set; }

    public PostalAddress BillingAddress { get; private set; }

    /// <summary>Langue de ses pièces imprimées (<see cref="DocumentLanguages"/>) ; <c>null</c> : le français.</summary>
    public string? PreferredLanguage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Supprimé par l'utilisateur : absent des listes et des choix de saisie, mais gardé en base, car ses devis et
    /// factures le désignent (la copie figée des pièces validées et le journal d'intégrité ne bougent pas).
    /// </summary>
    public bool IsArchived { get; private set; }

    /// <summary>Version de concurrence optimiste (ADR-0013), avancée par la persistance à chaque écriture.</summary>
    public long Version { get; private set; }

    /// <summary>Données brutes de création ou de modification, telles que saisies. Les objets-valeurs sont construits et validés ici.</summary>
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
        string? CountryCode,
        string? PreferredLanguage = null);

    /// <summary>Crée un client en accumulant <b>toutes</b> les erreurs de saisie (l'écran les affiche ensemble).</summary>
    public static Result<Customer> Create(CustomerId id, Draft draft, DateTimeOffset now)
    {
        Result<Values> values = Read(draft);
        if (values.IsFailure)
        {
            return Result<Customer>.Failure(values.Errors);
        }

        Values v = values.Value;
        Customer customer = new(id, v.Kind, v.Name, v.Siren, v.Siret, v.VatNumber, v.Email, v.Address, v.PreferredLanguage, now);
        customer.Raise(new CustomerCreated(id, now));
        return customer;
    }

    /// <summary>Remplace les données du client, avec les mêmes règles qu'à la création. Rien n'est modifié si une erreur est trouvée.</summary>
    public Result Update(Draft draft, DateTimeOffset now)
    {
        Result<Values> values = Read(draft);
        if (values.IsFailure)
        {
            return Result.Failure(values.Errors);
        }

        Values v = values.Value;
        Kind = v.Kind;
        Name = v.Name;
        Siren = v.Siren;
        Siret = v.Siret;
        VatNumber = v.VatNumber;
        Email = v.Email;
        BillingAddress = v.Address;
        PreferredLanguage = v.PreferredLanguage;
        Raise(new CustomerUpdated(Id, now));
        return Result.Success();
    }

    /// <summary>Retire le client des listes, ou l'y remet. Sans effet (et sans événement) si l'état est déjà celui demandé.</summary>
    public void SetArchived(bool archived, DateTimeOffset now)
    {
        if (IsArchived == archived)
        {
            return;
        }

        IsArchived = archived;
        Raise(new CustomerArchiveChanged(Id, archived, now));
    }

    private static Result<Values> Read(Draft draft)
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

        PartyIdentification ids = PartyIdentification.Read(draft.Siren, draft.Siret, draft.VatNumber, CustomerErrors.SiretSirenMismatch, errors);
        Siren? siren = ids.Siren;
        Siret? siret = ids.Siret;
        VatNumber? vat = ids.VatNumber;
        Email? email = PartyIdentification.Optional(draft.Email, SharedKernel.Email.Create, nameof(Email), errors);

        Result<PostalAddress> address = PostalAddress.Create(
            draft.AddressLine1, draft.AddressLine2, draft.PostalCode, draft.City, draft.CountryCode);
        errors.AddRange(address.Errors);

        string? language = DocumentLanguages.Normalize(draft.PreferredLanguage);
        if (language is null && !string.IsNullOrWhiteSpace(draft.PreferredLanguage))
        {
            errors.Add(CustomerErrors.PreferredLanguageInvalid);
        }

        if (draft.Kind == CustomerKind.Individual && (siren is not null || vat is not null))
        {
            errors.Add(CustomerErrors.IndividualCannotHaveCompanyIds);
        }

        // Le pays se lit indépendamment du reste de l'adresse : un code postal faux ne doit pas masquer l'absence de SIREN.
        Result<CountryCode> country = CountryCode.Create(string.IsNullOrWhiteSpace(draft.CountryCode) ? CountryCode.France.Value : draft.CountryCode);
        bool isFrench = country.IsSuccess && country.Value.IsFrance;
        if (draft.Kind == CustomerKind.Company && isFrench && siren is null && !PartyIdentification.HasIdentificationError(errors))
        {
            errors.Add(CustomerErrors.SirenRequired);
        }

        return errors.Count > 0
            ? Result<Values>.Failure(errors)
            : new Values(draft.Kind, name, siren, siret, vat, email, address.Value, language);
    }

    private sealed record Values(
        CustomerKind Kind, string Name, Siren? Siren, Siret? Siret, VatNumber? VatNumber, Email? Email, PostalAddress Address, string? PreferredLanguage);
}
