using System.ComponentModel.DataAnnotations;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Pricing;

/// <summary>
/// Un tarif d'une grille : prix HT imposé, ou remise en pourcentage sur le prix du catalogue (l'un ou l'autre).
/// </summary>
public sealed record PriceListEntryRequest
{
    public Guid CatalogItemId { get; set; }

    [Range(0d, 999_999_999.9999d, ErrorMessage = ErrorCodes.PriceList.EntryInvalid)]
    public decimal? UnitPriceExclTax { get; set; }

    [Range(0d, 100d, ErrorMessage = ErrorCodes.PriceList.EntryInvalid)]
    public decimal? DiscountPercent { get; set; }
}

/// <summary>Création ou modification d'une grille tarifaire et des clients qui en bénéficient.</summary>
public sealed record SavePriceListRequest
{
    [Required(ErrorMessage = ErrorCodes.PriceList.NameRequired)]
    [StringLength(100, ErrorMessage = ErrorCodes.PriceList.NameTooLong)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Clients rattachés. Un client n'a qu'une grille : le rattacher ici le retire de l'ancienne.</summary>
    public List<Guid> CustomerIds { get; set; } = [];

    public List<PriceListEntryRequest> Entries { get; set; } = [];
}

public sealed record PriceListEntryResponse(Guid CatalogItemId, string Reference, string Designation, decimal? UnitPriceExclTax, decimal? DiscountPercent);

public sealed record PriceListResponse(
    Guid Id,
    long Version,
    string Name,
    IReadOnlyList<Guid> CustomerIds,
    IReadOnlyList<PriceListEntryResponse> Entries,
    DateTimeOffset UpdatedAt);

public sealed record PriceListSummary(Guid Id, string Name, int CustomerCount, int EntryCount);

/// <summary>
/// Prix proposé pour un article et un client, à copier dans la ligne : prix du catalogue, prix de la grille du
/// client, ou prix du catalogue avec la remise de la grille.
/// </summary>
public sealed record ResolvedPrice(Guid CatalogItemId, decimal UnitPriceExclTax, decimal DiscountPercent, string VatRateCode, Guid? PriceListId);
