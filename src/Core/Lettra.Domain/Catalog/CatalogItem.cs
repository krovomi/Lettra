using Lettra.Calculations;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Catalog;

/// <summary>
/// Article du catalogue (bien ou service) que l'on reprend dans les lignes de devis et de facture.
/// Invariants :
/// <list type="bullet">
/// <item>une référence de 1 à 50 caractères (lettres, chiffres, <c>-</c>, <c>_</c>, <c>.</c>, <c>/</c>),
/// unique dans le dossier sans tenir compte de la casse (vérifié par l'application) ;</item>
/// <item>une désignation de 1 à 200 caractères, une description d'au plus 2 000 ;</item>
/// <item>un prix unitaire HT positif ou nul, porté à 4 décimales (règle de <see cref="Rounding"/>) ;</item>
/// <item>un taux de TVA et une unité connus ;</item>
/// <item>un article archivé n'est plus modifiable : on le réactive d'abord.</item>
/// </list>
/// Le catalogue ne fait que proposer : une ligne de document copie l'article au moment de la saisie,
/// et une modification ultérieure ne touche jamais un document déjà émis.
/// </summary>
public sealed class CatalogItem : AggregateRoot<CatalogItemId>
{
    public const int MaxReferenceLength = 50;
    public const int MaxDesignationLength = 200;
    public const int MaxDescriptionLength = 2000;
    public const decimal MaxUnitPrice = 999_999_999.9999m;

    // Constructeur réservé à la persistance.
    private CatalogItem(CatalogItemId id)
        : base(id)
    {
        Reference = string.Empty;
        ReferenceKey = string.Empty;
        Designation = string.Empty;
        Unit = null!;
        VatRate = null!;
    }

    private CatalogItem(CatalogItemId id, DateTimeOffset createdAt)
        : this(id)
    {
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public string Reference { get; private set; }

    /// <summary>Référence en majuscules : clé d'unicité et de recherche, identique sur les trois bases.</summary>
    public string ReferenceKey { get; private set; }

    public string Designation { get; private set; }

    public string? Description { get; private set; }

    public CatalogItemKind Kind { get; private set; }

    public UnitOfMeasure Unit { get; private set; }

    /// <summary>Prix unitaire hors taxes, en euros, à 4 décimales.</summary>
    public decimal UnitPriceExclTax { get; private set; }

    public VatRate VatRate { get; private set; }

    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Données brutes d'un article, telles que saisies (création comme modification).</summary>
    public sealed record Draft(
        string? Reference,
        string? Designation,
        string? Description,
        CatalogItemKind Kind,
        string? UnitCode,
        decimal UnitPriceExclTax,
        string? VatRateCode);

    /// <summary>Normalise une référence en clé de recherche (majuscules, sans espaces autour).</summary>
    public static string KeyOf(string? reference) => (reference ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Crée un article en accumulant toutes les erreurs de saisie.</summary>
    public static Result<CatalogItem> Create(CatalogItemId id, Draft draft, DateTimeOffset now)
    {
        CatalogItem item = new(id, now);
        Result applied = item.Apply(draft);
        if (applied.IsFailure)
        {
            return Result<CatalogItem>.Failure(applied.Errors);
        }

        item.Raise(new CatalogItemCreated(id, now));
        return item;
    }

    /// <summary>Remplace les données de l'article. Rien n'est modifié si une erreur est trouvée.</summary>
    public Result Update(Draft draft, DateTimeOffset now)
    {
        if (IsArchived)
        {
            return CatalogItemErrors.Archived;
        }

        Result applied = Apply(draft);
        if (applied.IsFailure)
        {
            return applied;
        }

        UpdatedAt = now;
        Raise(new CatalogItemUpdated(Id, now));
        return Result.Success();
    }

    /// <summary>Archive ou réactive l'article. Sans effet (et sans événement) si l'état est déjà celui demandé.</summary>
    public void SetArchived(bool archived, DateTimeOffset now)
    {
        if (IsArchived == archived)
        {
            return;
        }

        IsArchived = archived;
        UpdatedAt = now;
        Raise(new CatalogItemArchiveChanged(Id, archived, now));
    }

    private Result Apply(Draft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        List<Error> errors = [];

        string reference = (draft.Reference ?? string.Empty).Trim();
        if (reference.Length == 0)
        {
            errors.Add(CatalogItemErrors.ReferenceRequired);
        }
        else if (reference.Length > MaxReferenceLength)
        {
            errors.Add(CatalogItemErrors.ReferenceTooLong);
        }
        else if (!reference.All(IsReferenceChar))
        {
            errors.Add(CatalogItemErrors.ReferenceInvalid);
        }

        string designation = (draft.Designation ?? string.Empty).Trim();
        if (designation.Length == 0)
        {
            errors.Add(CatalogItemErrors.DesignationRequired);
        }
        else if (designation.Length > MaxDesignationLength)
        {
            errors.Add(CatalogItemErrors.DesignationTooLong);
        }

        string? description = string.IsNullOrWhiteSpace(draft.Description) ? null : draft.Description.Trim();
        if (description is { Length: > MaxDescriptionLength })
        {
            errors.Add(CatalogItemErrors.DescriptionTooLong);
        }

        if (!Enum.IsDefined(draft.Kind))
        {
            errors.Add(CatalogItemErrors.KindInvalid);
        }

        Result<UnitOfMeasure> unit = UnitOfMeasure.FromCode(draft.UnitCode);
        errors.AddRange(unit.Errors);

        Result<VatRate> vatRate = VatRate.FromCode(draft.VatRateCode);
        errors.AddRange(vatRate.Errors.Select(e => e.For(nameof(VatRate))));

        decimal price = Rounding.ToStorage(draft.UnitPriceExclTax);
        if (price < 0m)
        {
            errors.Add(CatalogItemErrors.PriceNegative);
        }
        else if (price > MaxUnitPrice)
        {
            errors.Add(CatalogItemErrors.PriceTooLarge);
        }

        if (errors.Count > 0)
        {
            return Result.Failure(errors);
        }

        Reference = reference;
        ReferenceKey = KeyOf(reference);
        Designation = designation;
        Description = description;
        Kind = draft.Kind;
        Unit = unit.Value;
        UnitPriceExclTax = price;
        VatRate = vatRate.Value;
        return Result.Success();
    }

    private static bool IsReferenceChar(char c) => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '/';
}
