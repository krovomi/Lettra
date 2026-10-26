namespace Lettra.Contracts.Insights;

/// <summary>
/// Le dossier tel qu'il était à un instant passé, rejoué depuis le journal inaltérable : chiffre d'affaires et TVA de
/// l'année, pièces et soldes des clients à cet instant, dernière clôture, derniers événements. Les chiffres « aujourd'hui »
/// permettent de voir ce qui a changé depuis.
/// </summary>
/// <param name="At">L'instant demandé.</param>
/// <param name="LegalDate">Son jour légal (heure de Paris) : l'année des totaux est la sienne.</param>
/// <param name="EntriesReplayed">Entrées du journal rejouées (jusqu'à l'instant compris).</param>
/// <param name="EntriesAfter">Entrées écrites après l'instant : ce qui s'est passé depuis.</param>
/// <param name="ChainIntact">Vrai si les entrées rejouées forment une chaîne intacte (empreintes, chaînage, sceaux).</param>
/// <param name="FirstEntryAt">La première entrée du journal : avant elle, le dossier est vide.</param>
/// <param name="Year">Chiffre d'affaires de l'année, avoirs déduits, tel qu'on le connaissait à l'instant.</param>
/// <param name="Months">Le même, mois par mois, jusqu'au mois de l'instant.</param>
/// <param name="Counts">Pièces et encaissements enregistrés jusqu'à l'instant.</param>
/// <param name="ReceivablesInclTax">Ce que les clients devaient à l'instant (factures moins avoirs et encaissements).</param>
/// <param name="ReceivablesNowInclTax">Ce qu'ils doivent aujourd'hui.</param>
/// <param name="Customers">Les clients qui avaient un solde à l'instant, ou qui en ont un aujourd'hui.</param>
/// <param name="OpenDocuments">Les factures et acomptes qui restaient dus à l'instant.</param>
/// <param name="LastClosing">La dernière clôture signée avant l'instant.</param>
/// <param name="LastEvents">Les derniers événements du journal avant l'instant, du plus récent au plus ancien.</param>
public sealed record DossierAsOfResponse(
    DateTimeOffset At,
    DateOnly LegalDate,
    long EntriesReplayed,
    long EntriesAfter,
    bool ChainIntact,
    DateTimeOffset? FirstEntryAt,
    DossierAsOfTotalsResponse Year,
    IReadOnlyList<DossierAsOfMonthResponse> Months,
    DossierAsOfCountsResponse Counts,
    decimal ReceivablesInclTax,
    decimal ReceivablesNowInclTax,
    IReadOnlyList<DossierAsOfCustomerResponse> Customers,
    IReadOnlyList<DossierAsOfDocumentResponse> OpenDocuments,
    DossierAsOfClosingResponse? LastClosing,
    IReadOnlyList<DossierAsOfEventResponse> LastEvents)
{
    /// <summary>Nombre d'événements listés dans <see cref="LastEvents"/>, au plus.</summary>
    public const int LastEventCount = 12;
}

/// <summary>Un chiffre d'affaires, avoirs déduits.</summary>
public sealed record DossierAsOfTotalsResponse(decimal ExclTax, decimal Vat, decimal InclTax);

/// <summary>Le chiffre d'affaires d'un mois (date d'émission des pièces), avoirs déduits.</summary>
public sealed record DossierAsOfMonthResponse(int Month, decimal ExclTax, decimal Vat, decimal InclTax, int Documents);

/// <summary>Ce qui avait été enregistré jusqu'à l'instant.</summary>
public sealed record DossierAsOfCountsResponse(int Quotes, int Invoices, int CreditNotes, int Payments, int Closings);

/// <summary>Le compte d'un client à l'instant : facturé, avoirs, encaissé, solde ; et son solde aujourd'hui.</summary>
public sealed record DossierAsOfCustomerResponse(
    Guid CustomerId,
    string CustomerName,
    decimal InvoicedInclTax,
    decimal CreditedInclTax,
    decimal PaidInclTax,
    decimal BalanceInclTax,
    decimal BalanceNowInclTax);

/// <summary>Nature d'une pièce rejouée (valeurs stables, ajout seulement).</summary>
public enum DossierAsOfDocumentKindDto
{
    Invoice = 0,
    DownPaymentInvoice = 1,
}

/// <summary>Une facture qui restait due à l'instant, et ce qu'il en reste aujourd'hui.</summary>
public sealed record DossierAsOfDocumentResponse(
    Guid DocumentId,
    string Number,
    DossierAsOfDocumentKindDto Kind,
    DateOnly IssueDate,
    Guid CustomerId,
    string CustomerName,
    decimal TotalInclTax,
    decimal OutstandingInclTax,
    decimal OutstandingNowInclTax);

/// <summary>Une clôture signée de la chaîne.</summary>
public sealed record DossierAsOfClosingResponse(string Number, DateOnly PeriodEnd, DateTimeOffset ClosedAt, decimal CumulativeInclTax);

/// <summary>Nature d'un événement du journal (mêmes valeurs que le journal ; ajout seulement).</summary>
public enum DossierAsOfEventKindDto
{
    QuoteValidated = 1,
    InvoiceValidated = 2,
    InvoiceIssued = 3,
    CreditNoteValidated = 4,
    CreditNoteIssued = 5,
    DocumentCancelled = 6,
    PlatformTransmitted = 7,
    PlatformStatusReceived = 8,
    PlatformPaymentReported = 9,
    PaymentRecorded = 10,
    PaymentReversed = 11,
    PeriodClosed = 100,
    KeyRotated = 101,
}

/// <summary>Un événement du journal : quand, quoi, et le numéro de la pièce ou de la clôture.</summary>
public sealed record DossierAsOfEventResponse(long Sequence, DateTimeOffset At, DossierAsOfEventKindDto Kind, string? Number, decimal? AmountInclTax);
