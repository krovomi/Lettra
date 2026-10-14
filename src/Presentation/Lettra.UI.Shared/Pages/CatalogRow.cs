using Lettra.Contracts.Catalog;
using Lettra.UI.Abstractions.Concurrency;

namespace Lettra.UI.Shared.Pages;

/// <summary>
/// Ligne de la grille du catalogue. <see cref="Key"/> reste stable pendant toute la vie de la ligne (y compris
/// avant sa création côté serveur, quand <see cref="Id"/> est encore nul) : la grille garde ainsi le focus.
/// </summary>
public sealed record CatalogRow(
    Guid Key,
    Guid? Id,
    string Reference,
    string Designation,
    string? Description,
    CatalogItemKindDto Kind,
    string UnitCode,
    decimal UnitPriceExclTax,
    string VatRateCode,
    decimal VatRatePercent,
    bool IsArchived)
{
    /// <summary>Fusion d'une saisie refusée en 412 avec l'article actuel du serveur (ADR-0013).</summary>
    public static FieldMerge<SaveCatalogItemRequest> Merge { get; } = new FieldMerge<SaveCatalogItemRequest>()
        .Field(nameof(SaveCatalogItemRequest.Reference), r => r.Reference, (r, v) => r with { Reference = v })
        .Field(nameof(SaveCatalogItemRequest.Designation), r => r.Designation, (r, v) => r with { Designation = v })
        .Field(nameof(SaveCatalogItemRequest.Description), r => r.Description, (r, v) => r with { Description = v })
        .Field(nameof(SaveCatalogItemRequest.Kind), r => r.Kind, (r, v) => r with { Kind = v })
        .Field(nameof(SaveCatalogItemRequest.UnitCode), r => r.UnitCode, (r, v) => r with { UnitCode = v })
        .Field(nameof(SaveCatalogItemRequest.UnitPriceExclTax), r => r.UnitPriceExclTax, (r, v) => r with { UnitPriceExclTax = v })
        .Field(nameof(SaveCatalogItemRequest.VatRateCode), r => r.VatRateCode, (r, v) => r with { VatRateCode = v });

    /// <summary>Version de l'article lue sur le serveur, renvoyée à chaque modification (<c>If-Match</c>).</summary>
    public long Version { get; init; }

    public static CatalogRow From(CatalogItemSummary item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new(item.Id, item.Id, item.Reference, item.Designation, item.Description, item.Kind, item.UnitCode, item.UnitPriceExclTax,
            item.VatRateCode, item.VatRatePercent, item.IsArchived)
        {
            Version = item.Version,
        };
    }

    public static CatalogRow From(CatalogItemResponse item, Guid key)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new(key, item.Id, item.Reference, item.Designation, item.Description, item.Kind, item.UnitCode, item.UnitPriceExclTax,
            item.VatRateCode, item.VatRatePercent, item.IsArchived)
        {
            Version = item.Version,
        };
    }

    /// <summary>Ligne vierge saisie en fin de grille, pas encore enregistrée.</summary>
    public static CatalogRow New(string unitCode, VatRateOption vatRate)
    {
        ArgumentNullException.ThrowIfNull(vatRate);
        return new(Guid.NewGuid(), null, string.Empty, string.Empty, null, CatalogItemKindDto.Service, unitCode, 0m,
            vatRate.Code, vatRate.Percent, false);
    }

    /// <summary>
    /// Requête complète d'enregistrement (création ou modification). La description, absente de la grille,
    /// est renvoyée telle quelle pour ne pas l'effacer.
    /// </summary>
    public SaveCatalogItemRequest ToRequest() => new()
    {
        Reference = Reference.Trim(),
        Designation = Designation.Trim(),
        Description = Description,
        Kind = Kind,
        UnitCode = UnitCode,
        UnitPriceExclTax = UnitPriceExclTax,
        VatRateCode = VatRateCode,
    };
}

/// <summary>Choix proposé par une liste F4 : code enregistré et libellé traduit.</summary>
public sealed record ChoiceOption(string Code, string Label);
