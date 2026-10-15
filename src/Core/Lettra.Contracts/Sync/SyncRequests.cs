using Lettra.Contracts.Catalog;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Suppliers;

namespace Lettra.Contracts.Sync;

/// <summary>
/// L'état d'un élément exprimé dans le contrat de sa saisie : c'est sur ce contrat que le serveur fusionne champ par
/// champ, et que le poste note l'état de départ d'une modification.
/// </summary>
public static class SyncRequests
{
    public static CreateCustomerRequest ToRequest(this CustomerResponse customer)
    {
        ArgumentNullException.ThrowIfNull(customer);
        return new CreateCustomerRequest
        {
            Kind = customer.Kind,
            Name = customer.Name,
            Siren = customer.Siren,
            Siret = customer.Siret,
            VatNumber = customer.VatNumber,
            Email = customer.Email,
            AddressLine1 = customer.AddressLine1,
            AddressLine2 = customer.AddressLine2,
            PostalCode = customer.PostalCode,
            City = customer.City,
            CountryCode = customer.CountryCode,
        };
    }

    public static CreateSupplierRequest ToRequest(this SupplierResponse supplier)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        return new CreateSupplierRequest
        {
            Name = supplier.Name,
            Siren = supplier.Siren,
            Siret = supplier.Siret,
            VatNumber = supplier.VatNumber,
            Email = supplier.Email,
            Iban = supplier.Iban,
            AddressLine1 = supplier.AddressLine1,
            AddressLine2 = supplier.AddressLine2,
            PostalCode = supplier.PostalCode,
            City = supplier.City,
            CountryCode = supplier.CountryCode,
        };
    }

    public static SaveCatalogItemRequest ToRequest(this CatalogItemResponse item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new SaveCatalogItemRequest
        {
            Reference = item.Reference,
            Designation = item.Designation,
            Description = item.Description,
            Kind = item.Kind,
            UnitCode = item.UnitCode,
            UnitPriceExclTax = item.UnitPriceExclTax,
            VatRateCode = item.VatRateCode,
        };
    }
}
