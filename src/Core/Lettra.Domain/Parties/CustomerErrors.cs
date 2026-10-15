using Lettra.Domain.Common;

namespace Lettra.Domain.Parties;

/// <summary>Codes d'erreur du module Tiers.</summary>
public static class CustomerErrors
{
    public const string NameRequiredCode = "Customer.NameRequired";
    public const string NameTooLongCode = "Customer.NameTooLong";
    public const string SirenRequiredCode = "Customer.SirenRequired";
    public const string SiretSirenMismatchCode = "Customer.SiretSirenMismatch";
    public const string IndividualCannotHaveCompanyIdsCode = "Customer.IndividualCannotHaveCompanyIds";
    public const string KindInvalidCode = "Customer.KindInvalid";
    public const string NotFoundCode = "Customer.NotFound";

    public static Error NameRequired => Error.Validation(NameRequiredCode, nameof(Customer.Name));
    public static Error NameTooLong => Error.Validation(NameTooLongCode, nameof(Customer.Name));
    public static Error SirenRequired => Error.Validation(SirenRequiredCode, nameof(Customer.Siren));
    public static Error SiretSirenMismatch => Error.Validation(SiretSirenMismatchCode, nameof(Customer.Siret));
    public static Error IndividualCannotHaveCompanyIds => Error.Validation(IndividualCannotHaveCompanyIdsCode, nameof(Customer.Siren));
    public static Error KindInvalid => Error.Validation(KindInvalidCode, nameof(Customer.Kind));
    public static Error NotFound => Error.NotFound(NotFoundCode);

    /// <summary>Langue d'impression hors de <see cref="DocumentLanguages.Supported"/>.</summary>
    public static Error PreferredLanguageInvalid => Error.Validation("Validation.Invalid", nameof(Customer.PreferredLanguage));
}
