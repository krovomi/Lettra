using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Billing;

/// <summary>Périodicité d'un modèle de facture récurrente.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RecurrenceFrequencyDto>))]
public enum RecurrenceFrequencyDto
{
    Monthly = 1,
    Quarterly = 2,
    Yearly = 3,
}

/// <summary>À échoir : la facture est datée du premier jour de sa période ; à terme échu : du dernier.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BillingTimingDto>))]
public enum BillingTimingDto
{
    InAdvance = 1,
    InArrears = 2,
}

/// <summary>Révision annuelle des prix, à chaque anniversaire de la date de début.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PriceRevisionKindDto>))]
public enum PriceRevisionKindDto
{
    None = 1,

    /// <summary>Pourcentage fixe (<see cref="SaveRecurringInvoiceRequest.RevisionPercent"/>).</summary>
    FixedPercent = 2,

    /// <summary>Indice Syntec : prix × dernier indice / indice de référence ; l'indice publié est saisi par l'utilisateur.</summary>
    SyntecIndex = 3,
}

/// <summary>Création ou modification d'un modèle de facture récurrente (même forme pour les deux).</summary>
public sealed record SaveRecurringInvoiceRequest
{
    [Required(ErrorMessage = ErrorCodes.RecurringInvoice.NameRequired)]
    [StringLength(120, ErrorMessage = ErrorCodes.RecurringInvoice.NameTooLong)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = ErrorCodes.Billing.CustomerRequired)]
    public Guid CustomerId { get; set; }

    public VatRegimeDto VatRegime { get; set; } = VatRegimeDto.Standard;

    /// <summary>Objet des factures ; la période y est ajoutée (« Honoraires (du 01/11/2026 au 30/11/2026) »).</summary>
    [StringLength(200, ErrorMessage = ErrorCodes.Billing.SubjectTooLong)]
    public string? Subject { get; set; }

    [StringLength(4000, ErrorMessage = ErrorCodes.Billing.NotesTooLong)]
    public string? Notes { get; set; }

    /// <summary>Conditions de paiement imprimées ; à défaut, celles de la fiche entreprise.</summary>
    [StringLength(500, ErrorMessage = ErrorCodes.Billing.PaymentTermsTooLong)]
    public string? PaymentTerms { get; set; }

    public List<DocumentLineRequest> Lines { get; set; } = [];

    public RecurrenceFrequencyDto Frequency { get; set; } = RecurrenceFrequencyDto.Monthly;

    public BillingTimingDto Timing { get; set; } = BillingTimingDto.InAdvance;

    /// <summary>Début de la première période ; les suivantes commencent au même quantième (ou au dernier jour du mois).</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Aucune période ne commence après cette date ; <c>null</c> : sans fin.</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>Valider chaque facture créée (numéro, journal) ; sinon elle reste en brouillon à relire.</summary>
    public bool ValidateAutomatically { get; set; }

    public PriceRevisionKindDto Revision { get; set; } = PriceRevisionKindDto.None;

    /// <summary>Hausse annuelle en pourcentage (révision <see cref="PriceRevisionKindDto.FixedPercent"/>).</summary>
    public decimal? RevisionPercent { get; set; }

    /// <summary>Indice Syntec des prix actuels (S0).</summary>
    public decimal? SyntecReferenceIndex { get; set; }

    /// <summary>Dernier indice Syntec publié (S1), saisi dès sa parution ; la révision attend tant qu'il égale S0.</summary>
    public decimal? SyntecLatestIndex { get; set; }
}

/// <summary>Suspendre ou reprendre un modèle.</summary>
public sealed record SetRecurringInvoiceActiveRequest
{
    public bool IsActive { get; set; }
}

/// <summary>Une facture créée par un modèle : sa période, son numéro une fois validée, et la révision appliquée juste avant.</summary>
public sealed record RecurringOccurrenceResponse(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    Guid InvoiceId,
    string? InvoiceNumber,
    InvoiceStatusDto? InvoiceStatus,
    decimal? RevisionCoefficient,
    DateTimeOffset CreatedAt);

/// <summary>
/// Modèle tel qu'affiché. <paramref name="NextPeriodStart"/>, <paramref name="NextPeriodEnd"/> et
/// <paramref name="NextIssueDate"/> décrivent la prochaine facture (<c>null</c> une fois la date de fin passée) ;
/// <paramref name="IsDue"/> dit qu'elle peut être créée aujourd'hui.
/// </summary>
public sealed record RecurringInvoiceResponse(
    Guid Id,
    long Version,
    string Name,
    Guid CustomerId,
    string CustomerName,
    VatRegimeDto VatRegime,
    string? Subject,
    string? Notes,
    string? PaymentTerms,
    IReadOnlyList<DocumentLineResponse> Lines,
    DocumentTotalsResponse Totals,
    RecurrenceFrequencyDto Frequency,
    BillingTimingDto Timing,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool IsActive,
    bool ValidateAutomatically,
    PriceRevisionKindDto Revision,
    decimal? RevisionPercent,
    decimal? SyntecReferenceIndex,
    decimal? SyntecLatestIndex,
    DateOnly? NextRevisionDate,
    bool RevisionAwaitsIndex,
    DateOnly? NextPeriodStart,
    DateOnly? NextPeriodEnd,
    DateOnly? NextIssueDate,
    bool IsDue,
    IReadOnlyList<RecurringOccurrenceResponse> Occurrences,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Ligne de la liste des modèles.</summary>
public sealed record RecurringInvoiceSummary(
    Guid Id,
    string Name,
    Guid CustomerId,
    string CustomerName,
    RecurrenceFrequencyDto Frequency,
    decimal TotalExclTax,
    bool IsActive,
    bool ValidateAutomatically,
    DateOnly? NextIssueDate,
    bool IsDue,
    bool RevisionAwaitsIndex,
    int InvoiceCount);

/// <summary>
/// Une facture créée par un passage de génération. <paramref name="ValidationError"/> : code de l'erreur quand la
/// validation automatique a été refusée (mentions manquantes, droits, séparation des tâches) ; la facture reste alors en brouillon.
/// </summary>
public sealed record RecurringRunItem(
    Guid RecurringInvoiceId,
    string Name,
    Guid InvoiceId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    bool Validated,
    string? Number,
    string? ValidationError);

/// <summary>
/// Résultat d'un passage : factures créées (et validées), et modèles en échec (code d'erreur, le modèle reste à la même période).
/// </summary>
public sealed record RecurringRunResponse(IReadOnlyList<RecurringRunItem> Created, IReadOnlyList<RecurringRunFailure> Failures)
{
    public int CreatedCount => Created.Count;

    public int ValidatedCount => Created.Count(i => i.Validated);
}

/// <summary>Un modèle qui n'a pas pu produire sa facture (client supprimé, ligne devenue invalide…).</summary>
public sealed record RecurringRunFailure(Guid RecurringInvoiceId, string Name, string Code);

/// <summary>Tri de la liste des modèles.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RecurringInvoiceSortDto>))]
public enum RecurringInvoiceSortDto
{
    /// <summary>Prochaine facture d'abord (les modèles terminés ou suspendus en dernier).</summary>
    NextIssueDate = 1,
    Name = 2,
    Customer = 3,
    Amount = 4,
}
