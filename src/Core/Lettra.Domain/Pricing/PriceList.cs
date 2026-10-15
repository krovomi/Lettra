using Lettra.Calculations;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Pricing;

/// <summary>Identifiant d'une grille tarifaire.</summary>
public readonly record struct PriceListId(Guid Value)
{
    public static PriceListId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public static class PriceListErrors
{
    public const string NameRequiredCode = "PriceList.NameRequired";
    public const string NameTooLongCode = "PriceList.NameTooLong";
    public const string EntryInvalidCode = "PriceList.EntryInvalid";
    public const string EntryDuplicateCode = "PriceList.EntryDuplicate";
    public const string CatalogItemUnknownCode = "PriceList.CatalogItemUnknown";
    public const string CustomerUnknownCode = "PriceList.CustomerUnknown";
    public const string NotFoundCode = "PriceList.NotFound";

    public static Error NameRequired => Error.Validation(NameRequiredCode, nameof(PriceList.Name));
    public static Error NameTooLong => Error.Validation(NameTooLongCode, nameof(PriceList.Name));
    public static Error EntryDuplicate => Error.Validation(EntryDuplicateCode, nameof(PriceList.Entries));
    public static Error CatalogItemUnknown => Error.Validation(CatalogItemUnknownCode, nameof(PriceList.Entries));
    public static Error CustomerUnknown => Error.Validation(CustomerUnknownCode, nameof(PriceList.Customers));
    public static Error NotFound => Error.NotFound(NotFoundCode);
    internal static Error EntryInvalid(int index) => Error.Validation(EntryInvalidCode, $"Entries[{index}]");
}

/// <summary>Un tarif : prix HT imposé, ou remise sur le prix du catalogue.</summary>
public sealed record PriceListEntry(CatalogItemId CatalogItemId, decimal? UnitPriceExclTax, decimal? DiscountPercent);

/// <summary>Un client rattaché à la grille.</summary>
public sealed record PriceListCustomer(CustomerId CustomerId);

/// <summary>Un tarif saisi.</summary>
public sealed record PriceListEntryDraft(Guid CatalogItemId, decimal? UnitPriceExclTax, decimal? DiscountPercent);

/// <summary>
/// Grille tarifaire (tarifs par client, venus de la phase 2). Invariants : un nom, chaque article au plus une fois,
/// chaque tarif donne soit un prix HT positif ou nul, soit une remise de 0 à 100 %. Qu'un client n'ait qu'une grille
/// est garanti par un index unique et par le cas d'usage, qui le retire de son ancienne grille.
/// La grille ne fait que proposer : la ligne de pièce copie le prix au moment de la saisie.
/// </summary>
public sealed class PriceList : AggregateRoot<PriceListId>, IVersioned
{
    public const int MaxNameLength = 100;

    private readonly List<PriceListEntry> _entries = [];
    private readonly List<PriceListCustomer> _customers = [];

    // Constructeur réservé à la persistance.
    private PriceList(PriceListId id)
        : base(id)
    {
        Name = string.Empty;
    }

    public string Name { get; private set; }

    public IReadOnlyList<PriceListEntry> Entries => _entries;

    public IReadOnlyList<PriceListCustomer> Customers => _customers;

    public long Version { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<PriceList> Create(PriceListId id, string? name, IReadOnlyList<PriceListEntryDraft> entries, IReadOnlyList<Guid> customers, DateTimeOffset now)
    {
        PriceList list = new(id);
        Result applied = list.Apply(name, entries, customers, now);
        return applied.IsSuccess ? list : Result<PriceList>.Failure(applied.Errors);
    }

    public Result Update(string? name, IReadOnlyList<PriceListEntryDraft> entries, IReadOnlyList<Guid> customers, DateTimeOffset now) =>
        Apply(name, entries, customers, now);

    /// <summary>Retire un client (rattaché à une autre grille).</summary>
    public void RemoveCustomer(CustomerId customerId, DateTimeOffset now)
    {
        if (_customers.RemoveAll(c => c.CustomerId == customerId) > 0)
        {
            UpdatedAt = now;
            Version++;
        }
    }

    /// <summary>Prix proposé pour un article : prix imposé, ou prix du catalogue avec la remise de la grille.</summary>
    public (decimal UnitPriceExclTax, decimal DiscountPercent)? PriceFor(CatalogItemId itemId, decimal catalogPrice)
    {
        PriceListEntry? entry = _entries.FirstOrDefault(e => e.CatalogItemId == itemId);
        return entry switch
        {
            null => null,
            { UnitPriceExclTax: { } price } => (price, 0m),
            { DiscountPercent: { } discount } => (catalogPrice, discount),
            _ => null,
        };
    }

    private Result Apply(string? name, IReadOnlyList<PriceListEntryDraft> entries, IReadOnlyList<Guid> customers, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(customers);
        List<Error> errors = [];
        string clean = (name ?? string.Empty).Trim();
        if (clean.Length == 0)
        {
            errors.Add(PriceListErrors.NameRequired);
        }
        else if (clean.Length > MaxNameLength)
        {
            errors.Add(PriceListErrors.NameTooLong);
        }

        List<PriceListEntry> parsed = [];
        for (int i = 0; i < entries.Count; i++)
        {
            PriceListEntryDraft e = entries[i];
            bool hasPrice = e.UnitPriceExclTax is not null;
            bool hasDiscount = e.DiscountPercent is not null;
            if (hasPrice == hasDiscount || e.UnitPriceExclTax is < 0m or > CatalogItem.MaxUnitPrice || e.DiscountPercent is < 0m or > 100m || e.CatalogItemId == Guid.Empty)
            {
                errors.Add(PriceListErrors.EntryInvalid(i));
                continue;
            }

            parsed.Add(new PriceListEntry(
                new CatalogItemId(e.CatalogItemId),
                e.UnitPriceExclTax is { } price ? Rounding.ToStorage(price) : null,
                e.DiscountPercent is { } discount ? Rounding.ToStorage(discount) : null));
        }

        if (parsed.GroupBy(e => e.CatalogItemId).Any(g => g.Count() > 1))
        {
            errors.Add(PriceListErrors.EntryDuplicate);
        }

        if (errors.Count > 0)
        {
            return Result.Failure(errors);
        }

        Name = clean;
        _entries.Clear();
        _entries.AddRange(parsed);
        _customers.Clear();
        _customers.AddRange(customers.Distinct().Select(c => new PriceListCustomer(new CustomerId(c))));
        UpdatedAt = now;
        Version++;
        return Result.Success();
    }
}
