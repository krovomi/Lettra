using Lettra.Application.Features.Catalog;
using Lettra.Application.Features.Parties;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Suppliers;
using Lettra.Domain.Catalog;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Mapping;

/// <summary>
/// Passage Contracts ↔ Application des modules Tiers (fournisseurs) et Catalogue, partagé par l'API et la
/// gateway in-process (ADR-0005). Les erreurs passent par <see cref="ContractMapper.ToErrorDetails"/>.
/// </summary>
public static class PartiesAndCatalogMapper
{
    public static CreateSupplierCommand ToCommand(this CreateSupplierRequest request, DossierId dossier, SupplierId? id = null) =>
        new(dossier, request.ToDraft(), id ?? SupplierId.New());

    /// <summary>Modification : même contrat qu'à la création, plus la version lue (<c>If-Match</c>).</summary>
    public static UpdateSupplierCommand ToUpdateCommand(this CreateSupplierRequest request, DossierId dossier, SupplierId id, long? expectedVersion) =>
        new(dossier, id, request.ToDraft(), expectedVersion);

    public static Supplier.Draft ToDraft(this CreateSupplierRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new Supplier.Draft(
                request.Name,
                request.Siren,
                request.Siret,
                request.VatNumber,
                request.Email,
                request.Iban,
                request.AddressLine1,
                request.AddressLine2,
                request.PostalCode,
                request.City,
                request.CountryCode);
    }

    public static SupplierResponse ToResponse(this Supplier supplier)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        return new SupplierResponse(
            supplier.Id.Value,
            supplier.Name,
            supplier.Siren?.Value,
            supplier.Siret?.Value,
            supplier.VatNumber?.Value,
            supplier.Email?.Value,
            supplier.Iban?.Value,
            supplier.Address.Line1,
            supplier.Address.Line2,
            supplier.Address.PostalCode,
            supplier.Address.City,
            supplier.Address.Country.Value,
            supplier.CreatedAt)
        {
            Version = supplier.Version,
            IsArchived = supplier.IsArchived,
        };
    }

    public static CatalogItem.Draft ToDraft(this SaveCatalogItemRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new CatalogItem.Draft(
            request.Reference,
            request.Designation,
            request.Description,
            (CatalogItemKind)(int)request.Kind,
            request.UnitCode,
            request.UnitPriceExclTax,
            request.VatRateCode);
    }

    public static CreateCatalogItemCommand ToCreateCommand(this SaveCatalogItemRequest request, DossierId dossier, CatalogItemId? id = null) =>
        new(dossier, request.ToDraft(), id ?? CatalogItemId.New());

    public static UpdateCatalogItemCommand ToUpdateCommand(
        this SaveCatalogItemRequest request, DossierId dossier, CatalogItemId id, long? expectedVersion = null) =>
        new(dossier, id, request.ToDraft(), expectedVersion);

    public static CatalogItemResponse ToResponse(this CatalogItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new CatalogItemResponse(
            item.Id.Value,
            item.Reference,
            item.Designation,
            item.Description,
            (CatalogItemKindDto)(int)item.Kind,
            item.Unit.Code,
            item.UnitPriceExclTax,
            item.VatRate.Code,
            item.VatRate.Percent,
            item.IsArchived,
            item.CreatedAt,
            item.UpdatedAt)
        {
            Version = item.Version,
        };
    }

    public static CatalogItemSummary ToSummary(this CatalogItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new CatalogItemSummary(
            item.Id.Value,
            item.Reference,
            item.Designation,
            item.Description,
            (CatalogItemKindDto)(int)item.Kind,
            item.Unit.Code,
            item.UnitPriceExclTax,
            item.VatRate.Code,
            item.VatRate.Percent,
            item.IsArchived)
        {
            Version = item.Version,
        };
    }
}
