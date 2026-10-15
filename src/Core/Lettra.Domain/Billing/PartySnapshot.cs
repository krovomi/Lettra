using Lettra.Domain.Parties;

namespace Lettra.Domain.Billing;

/// <summary>
/// Copie d'un tiers sur une pièce : le client à chaque enregistrement du brouillon, l'émetteur (fiche entreprise) à la
/// validation. Une fois la pièce validée, la copie ne bouge plus, même si la fiche d'origine change.
/// </summary>
public sealed record PartySnapshot(
    string Name,
    string AddressLine1,
    string? AddressLine2,
    string PostalCode,
    string City,
    string CountryCode,
    string? Siren,
    string? Siret,
    string? VatNumber,
    string? LegalForm,
    decimal? ShareCapital,
    string? Rcs,
    bool IsBusiness)
{
    public static PartySnapshot Of(Customer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);
        return new(
            customer.Name,
            customer.BillingAddress.Line1,
            customer.BillingAddress.Line2,
            customer.BillingAddress.PostalCode,
            customer.BillingAddress.City,
            customer.BillingAddress.Country.Value,
            customer.Siren?.Value,
            customer.Siret?.Value,
            customer.VatNumber?.Value,
            LegalForm: null,
            ShareCapital: null,
            Rcs: null,
            IsBusiness: customer.Kind == CustomerKind.Company);
    }

    /// <summary>Adresse sur une ligne, comme imprimée.</summary>
    public string SingleLineAddress =>
        string.Join(", ", new[] { AddressLine1, AddressLine2, $"{PostalCode} {City}".Trim(), CountryCode == "FR" ? null : CountryCode }
            .Where(p => !string.IsNullOrWhiteSpace(p)));
}
