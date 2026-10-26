namespace Lettra.Contracts.Tax;

/// <summary>
/// Une tranche du tableau des factures émises non réglées à la clôture (art. D441-6) : nombre de factures, montant TTC
/// restant dû et part du chiffre d'affaires TTC de l'exercice. <paramref name="Bracket"/> va de 0 (« 0 jour ») à 4
/// (« 91 jours et plus »).
/// </summary>
public sealed record PaymentDelayBracketResponse(int Bracket, int Count, decimal AmountInclTax, decimal? TurnoverPercent);

/// <summary>Une facture émise non réglée à la clôture : son retard ce jour-là, sa tranche et ce qui restait dû.</summary>
public sealed record PaymentDelayInvoiceResponse(
    Guid InvoiceId,
    string Number,
    string CustomerName,
    DateOnly IssueDate,
    DateOnly DueDate,
    int DaysLate,
    int? Bracket,
    decimal OutstandingInclTax,
    bool Disputed);

/// <summary>Une facture à un professionnel dont l'échéance dépasse le plafond légal (60 jours ou 45 jours fin de mois).</summary>
public sealed record PaymentTermBreachResponse(
    Guid InvoiceId,
    string Number,
    string CustomerName,
    DateOnly IssueDate,
    DateOnly DueDate,
    DateOnly LegalCeiling,
    int DaysOver);

/// <summary>
/// Délais de paiement d'un exercice, côté factures émises : le tableau du rapport de gestion à la date de clôture
/// (partie A par tranche, total « 1 jour et plus », partie B des factures litigieuses exclues, partie C du délai de
/// référence), les factures non réglées derrière, et les factures dont l'échéance dépasse le plafond légal.
/// </summary>
public sealed record PaymentDelaysResponse(
    DateOnly FiscalYearStart,
    DateOnly ClosingDate,
    decimal TurnoverInclTax,
    IReadOnlyList<PaymentDelayBracketResponse> Brackets,
    PaymentDelayBracketResponse Late,
    int ExcludedCount,
    decimal ExcludedAmountInclTax,
    int? ContractualDays,
    int NotDueCount,
    decimal NotDueAmountInclTax,
    IReadOnlyList<PaymentDelayInvoiceResponse> Invoices,
    IReadOnlyList<PaymentTermBreachResponse> TermBreaches);
