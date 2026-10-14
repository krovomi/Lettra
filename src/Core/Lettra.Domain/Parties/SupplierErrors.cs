using Lettra.Domain.Common;

namespace Lettra.Domain.Parties;

/// <summary>Codes d'erreur des fournisseurs (module Tiers).</summary>
public static class SupplierErrors
{
    public const string NameRequiredCode = "Supplier.NameRequired";
    public const string NameTooLongCode = "Supplier.NameTooLong";
    public const string SirenRequiredCode = "Supplier.SirenRequired";
    public const string SiretSirenMismatchCode = "Supplier.SiretSirenMismatch";
    public const string NotFoundCode = "Supplier.NotFound";

    public static Error NameRequired => Error.Validation(NameRequiredCode, nameof(Supplier.Name));
    public static Error NameTooLong => Error.Validation(NameTooLongCode, nameof(Supplier.Name));
    public static Error SirenRequired => Error.Validation(SirenRequiredCode, nameof(Supplier.Siren));
    public static Error SiretSirenMismatch => Error.Validation(SiretSirenMismatchCode, nameof(Supplier.Siret));
    public static Error NotFound => Error.NotFound(NotFoundCode);
}
