using Lettra.Calculations;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Billing;

/// <summary>Ventilation de la TVA figée sur une pièce : catégorie, taux, base, TVA, motif d'exonération.</summary>
public sealed record VatSummary(string Category, decimal RatePercent, decimal BaseExclTax, decimal VatAmount, string? ExemptionCode);

/// <summary>
/// Ce que les devis et les factures ont en commun : client, régime de TVA, lignes, totaux, version et auteurs.
/// Les totaux sont recalculés à chaque modification du brouillon et figés à la validation.
/// </summary>
public abstract class BillingDocument<TId> : AggregateRoot<TId>, IVersioned
    where TId : struct, IEquatable<TId>
{
    public const int MaxLines = 500;
    public const int MaxSubjectLength = 200;
    public const int MaxNotesLength = 4000;

    private static readonly Comparison<DocumentLine> LineOrder = (a, b) => a.Position.CompareTo(b.Position);

    private static readonly Comparison<VatSummary> VatSummaryOrder = (a, b) =>
    {
        int byRate = b.RatePercent.CompareTo(a.RatePercent);
        return byRate != 0 ? byRate : string.CompareOrdinal(a.Category, b.Category);
    };

    private readonly List<DocumentLine> _lines = [];
    private readonly List<VatSummary> _vatSummaries = [];

    protected BillingDocument(TId id)
        : base(id)
    {
        SeriesCode = string.Empty;
        Customer = null!;
        LegalMentions = string.Empty;
    }

    /// <summary>Numéro attribué à la validation, dans la série <see cref="SeriesCode"/>.</summary>
    public string? Number { get; protected set; }

    /// <summary>Série de numérotation : <c>FAC</c>, <c>DEV</c> ; une série par poste en phase 4 (hors ligne).</summary>
    public string SeriesCode { get; protected set; }

    /// <summary>Poste qui a numéroté la pièce hors ligne (phase 4) ; <c>null</c> quand c'est le serveur.</summary>
    public Guid? DeviceId { get; protected set; }

    public CustomerId CustomerId { get; protected set; }

    public PartySnapshot Customer { get; protected set; }

    public VatRegime VatRegime { get; protected set; }

    public string? Subject { get; protected set; }

    public string? Notes { get; protected set; }

    public DateOnly? IssueDate { get; protected set; }

    /// <summary>Lignes dans l'ordre de leur position, quel que soit l'ordre rendu par la base.</summary>
    public IReadOnlyList<DocumentLine> Lines
    {
        get
        {
            EnsureOrdered(_lines, LineOrder);
            return _lines;
        }
    }

    /// <summary>Récapitulatif de TVA dans l'ordre du calcul (taux décroissant, puis catégorie), quel que soit l'ordre rendu par la base.</summary>
    public IReadOnlyList<VatSummary> VatSummaries
    {
        get
        {
            EnsureOrdered(_vatSummaries, VatSummaryOrder);
            return _vatSummaries;
        }
    }

    public decimal TotalExclTax { get; private set; }

    public decimal TotalVat { get; private set; }

    public decimal TotalInclTax { get; private set; }

    /// <summary>Mentions légales imprimées en pied, une par ligne (TVA, recouvrement, débits…), figées à la validation.</summary>
    public string LegalMentions { get; protected set; }

    public IReadOnlyList<string> LegalMentionList => LegalMentions.Length == 0 ? [] : LegalMentions.Split('\n');

    /// <summary>
    /// Version de concurrence optimiste (ADR-0013), exposée en <c>ETag</c>. Avancée ici à chaque modification, car
    /// une ligne seule peut changer sans toucher l'en-tête ; la persistance la laisse alors telle quelle.
    /// </summary>
    public long Version { get; private set; }

    public Guid CreatedBy { get; private set; }

    public Guid LastModifiedBy { get; private set; }

    public Guid? ValidatedBy { get; protected set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? ValidatedAt { get; protected set; }

    public abstract bool IsDraft { get; }

    /// <summary>Seul un brouillon se supprime : une pièce validée est numérotée et inscrite au journal d'intégrité.</summary>
    public Result CanBeDeleted() => IsDraft ? Result.Success() : BillingErrors.NotDraft;

    /// <summary>Catégorie d'opération (mention 2026) : biens, services, ou mixte.</summary>
    public OperationCategory OperationCategory =>
        _lines.Where(l => l.Nature != RevenueNature.DownPayment).Select(l => l.Kind).Distinct().Count() switch
        {
            0 => OperationCategory.Services,
            1 => _lines.First(l => l.Nature != RevenueNature.DownPayment).Kind == Catalog.CatalogItemKind.Good ? OperationCategory.Goods : OperationCategory.Services,
            _ => OperationCategory.Mixed,
        };

    /// <summary>Totaux tels que les calcule la règle unique, pour les écritures comptables et les contrôles.</summary>
    public DocumentTotals ComputeTotals() => DocumentCalculator.Compute(_lines.Select(l => l.ToInput()));

    protected void Created(Guid userId, DateTimeOffset now)
    {
        CreatedBy = userId;
        LastModifiedBy = userId;
        CreatedAt = now;
        UpdatedAt = now;
        Version = 1;
    }

    /// <summary>Auteurs et dates d'une pièce reconstruite telle qu'elle a été figée ailleurs ; version initiale.</summary>
    protected void Replicated(Guid createdBy, DateTimeOffset createdAt, Guid lastModifiedBy, DateTimeOffset updatedAt)
    {
        CreatedBy = createdBy;
        LastModifiedBy = lastModifiedBy;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        Version = 1;
    }

    protected void Touch(Guid userId, DateTimeOffset now)
    {
        LastModifiedBy = userId;
        UpdatedAt = now;
        Version++;
    }

    /// <summary>Contrôle les champs communs et remplace les lignes. Rien ne change si une erreur est trouvée.</summary>
    protected Result ApplyContent(
        PartySnapshot customer,
        CustomerId customerId,
        VatRegime regime,
        string? subject,
        string? notes,
        IReadOnlyList<LineDraft> lines,
        Func<int, RevenueNature?>? natureOf = null,
        List<Error>? errors = null)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(lines);
        errors ??= [];

        if (!Enum.IsDefined(regime))
        {
            errors.Add(BillingErrors.VatRegimeInvalid);
            regime = VatRegime.Standard;
        }

        string? cleanSubject = Clean(subject);
        if (cleanSubject is { Length: > MaxSubjectLength })
        {
            errors.Add(BillingErrors.SubjectTooLong);
        }

        string? cleanNotes = Clean(notes);
        if (cleanNotes is { Length: > MaxNotesLength })
        {
            errors.Add(BillingErrors.NotesTooLong);
        }

        if (lines.Count == 0)
        {
            errors.Add(BillingErrors.LinesRequired);
        }
        else if (lines.Count > MaxLines)
        {
            errors.Add(BillingErrors.TooManyLines);
        }

        List<DocumentLine> parsed = [];
        for (int i = 0; i < Math.Min(lines.Count, MaxLines); i++)
        {
            Result<DocumentLine> line = DocumentLine.Create(i, lines[i], regime, natureOf?.Invoke(i));
            if (line.IsSuccess)
            {
                parsed.Add(line.Value);
            }
            else
            {
                errors.AddRange(line.Errors);
            }
        }

        if (errors.Count > 0)
        {
            return Result.Failure(errors);
        }

        CustomerId = customerId;
        Customer = customer;
        VatRegime = regime;
        Subject = cleanSubject;
        Notes = cleanNotes;
        _lines.Clear();
        _lines.AddRange(parsed);
        RefreshTotals();
        return Result.Success();
    }

    /// <summary>Met à jour la copie du client tant que la pièce est un brouillon (sa fiche a pu changer).</summary>
    protected void RefreshCustomer(PartySnapshot customer) => Customer = customer;

    private void RefreshTotals()
    {
        DocumentTotals totals = ComputeTotals();
        _vatSummaries.Clear();
        _vatSummaries.AddRange(totals.Breakdown.Select(b => new VatSummary(
            b.VatCategory,
            b.VatRatePercent,
            b.BaseExclTax,
            b.VatAmount,
            VatCategoryCodes.IsTaxed(b.VatCategory)
                ? null
                : Lines.First(l => l.VatCategory == b.VatCategory).VatExemptionCode)));
        TotalExclTax = totals.TotalExclTax;
        TotalVat = totals.TotalVat;
        TotalInclTax = totals.TotalInclTax;
    }

    /// <summary>Textes légaux de TVA de la pièce (un par motif d'exonération ou d'autoliquidation), sans doublon.</summary>
    protected IEnumerable<string> VatLegalTexts() =>
        VatSummaries
            .Select(v => VatTreatment.LegalTextOf(VatRegime, v.ExemptionCode))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal);

    protected static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // PostgreSQL et SQL Server ne garantissent pas l'ordre des collections possédées : on le rétablit à la lecture.
    // Les clés (position ; catégorie et taux) sont uniques, le tri est donc déterministe.
    private static void EnsureOrdered<T>(List<T> items, Comparison<T> order)
    {
        for (int i = 1; i < items.Count; i++)
        {
            if (order(items[i - 1], items[i]) > 0)
            {
                items.Sort(order);
                return;
            }
        }
    }
}

/// <summary>Catégorie d'opération (valeurs identiques à <c>OperationCategoryDto</c>).</summary>
public enum OperationCategory
{
    Goods = 1,
    Services = 2,
    Mixed = 3,
}
