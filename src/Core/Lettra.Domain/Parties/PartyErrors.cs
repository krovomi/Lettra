using Lettra.Domain.Common;

namespace Lettra.Domain.Parties;

/// <summary>Codes d'erreur communs aux tiers (clients et fournisseurs).</summary>
public static class PartyErrors
{
    public const string VatNumberSirenMismatchCode = "Party.VatNumberSirenMismatch";

    /// <summary>Un numéro de TVA français contient le SIREN : il doit être celui du tiers.</summary>
    public static Error VatNumberSirenMismatch => Error.Validation(VatNumberSirenMismatchCode, "VatNumber");
}
