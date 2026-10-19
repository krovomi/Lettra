using System.Text.Json.Serialization;
using Lettra.Contracts.Billing;

namespace Lettra.Contracts.Tax;

/// <summary>Formulaire de la déclaration de TVA : CA3 (réel normal, mini-réel) ou CA12 (réel simplifié).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VatReturnFormDto>))]
public enum VatReturnFormDto
{
    /// <summary>Déclaration n° 3310-CA3-SD, mensuelle ou trimestrielle.</summary>
    Ca3 = 1,

    /// <summary>Déclaration annuelle n° 3517-S-SD, avec deux acomptes semestriels.</summary>
    Ca12 = 2,
}

/// <summary>Rythme de la CA3 : mensuel, ou trimestriel quand la TVA due de l'année est inférieure à 4 000 €.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VatPeriodicityDto>))]
public enum VatPeriodicityDto
{
    Monthly = 1,
    Quarterly = 2,
}

/// <summary>Gravité d'un contrôle de cohérence : tout va bien, à regarder, ou à corriger avant de déposer.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TaxCheckSeverityDto>))]
public enum TaxCheckSeverityDto
{
    Ok = 1,
    Warning = 2,
    Blocking = 3,
}

/// <summary>
/// Une pièce derrière une case : la facture, l'acompte ou l'avoir, sa date d'exigibilité, sa base et sa taxe dans la
/// case (négatives pour un avoir).
/// </summary>
public sealed record TaxSourceResponse(
    Guid InvoiceId,
    InvoiceKindDto Kind,
    string PieceNumber,
    DateOnly Date,
    Guid CustomerId,
    string CustomerName,
    decimal BaseExclTax,
    decimal VatAmount);

/// <summary>
/// Une case du brouillon. <see cref="Key"/> est stable (clé de libellé <c>Tax.Box.{Key}</c>), <see cref="Section"/>
/// regroupe les cases (clé <c>Tax.Section.{Section}</c>), <see cref="Line"/> est le numéro de ligne du formulaire quand
/// il est sûr. Une case de base n'a pas de taxe, une case de total n'a pas de base. <see cref="Sources"/> liste les
/// pièces qui la composent. Les montants sont au centime ; le formulaire les demande arrondis à l'euro.
/// </summary>
public sealed record VatReturnBoxResponse(
    string Key,
    string Section,
    string? Line,
    decimal? RatePercent,
    decimal? BaseExclTax,
    decimal? VatAmount,
    IReadOnlyList<TaxSourceResponse> Sources);

/// <summary>Un contrôle de cohérence : son code (clé <c>Tax.Check.{Code}</c>), sa gravité, les montants comparés, les pièces en cause.</summary>
public sealed record TaxCheckResponse(
    string Code,
    TaxCheckSeverityDto Severity,
    decimal? Expected = null,
    decimal? Actual = null,
    int? Count = null,
    IReadOnlyList<string>? Pieces = null);

/// <summary>Un acompte semestriel de la CA12 : 55 % en juillet, 40 % en décembre de la TVA due de l'exercice précédent.</summary>
public sealed record VatInstallmentResponse(int Number, DateOnly DueDate, decimal Amount, bool Exempt);

/// <summary>
/// Brouillon de déclaration de TVA, partie ventes, calculé à partir des pièces validées. La TVA déductible (achats)
/// n'est pas encore calculée : <see cref="DeductibleVat"/> vaut zéro et le brouillon le dit. Rien n'est déposé.
/// </summary>
public sealed record VatReturnResponse(
    VatReturnFormDto Form,
    VatDeclarationRegimeDto? Regime,
    DateOnly From,
    DateOnly To,
    DateOnly DueDate,
    IReadOnlyList<VatReturnBoxResponse> Boxes,
    decimal GrossVat,
    decimal DeductibleVat,
    decimal NetVatDue,
    decimal VatCredit,
    IReadOnlyList<TaxCheckResponse> Checks,
    IReadOnlyList<VatInstallmentResponse> Installments,
    bool VatOnReceipts,
    DateTimeOffset? FiledAt);

/// <summary>Une ligne de la DES : un client de l'Union, son numéro de TVA, le montant HT des prestations du mois arrondi à l'euro.</summary>
public sealed record DesLineResponse(
    int LineNumber,
    string CustomerVatNumber,
    string CountryCode,
    Guid CustomerId,
    string CustomerName,
    decimal Amount,
    IReadOnlyList<TaxSourceResponse> Sources);

/// <summary>Déclaration européenne de services d'un mois : prestations à des assujettis de l'Union, TVA due par le preneur.</summary>
public sealed record DesResponse(
    int Year,
    int Month,
    DateOnly DueDate,
    IReadOnlyList<DesLineResponse> Lines,
    decimal Total,
    IReadOnlyList<TaxCheckResponse> Checks,
    DateTimeOffset? FiledAt);

/// <summary>Une obligation du calendrier fiscal.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FiscalObligationDto>))]
public enum FiscalObligationDto
{
    /// <summary>CA3 du mois ou du trimestre.</summary>
    VatReturnCa3 = 1,

    /// <summary>CA12 annuelle.</summary>
    VatReturnCa12 = 2,

    /// <summary>Acompte semestriel de TVA (réel simplifié).</summary>
    VatInstallment = 3,

    /// <summary>Déclaration européenne de services du mois.</summary>
    Des = 4,

    /// <summary>Cotisation foncière des entreprises.</summary>
    Cfe = 5,

    /// <summary>Acompte trimestriel d'impôt sur les sociétés.</summary>
    CorporateTaxInstallment = 6,

    /// <summary>Solde de l'impôt sur les sociétés.</summary>
    CorporateTaxBalance = 7,

    /// <summary>Liasse fiscale de l'exercice.</summary>
    TaxReturn = 8,
}

/// <summary>Où en est une échéance : à venir, dans les 7 jours, dépassée sans dépôt marqué, déposée, ou sans objet.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FiscalDeadlineStateDto>))]
public enum FiscalDeadlineStateDto
{
    Upcoming = 1,
    DueSoon = 2,
    Overdue = 3,
    Filed = 4,
    Exempt = 5,

    /// <summary>Passée avant le début du suivi (premier enregistrement des réglages) : ni en retard, ni déposée.</summary>
    Past = 6,
}

/// <summary>
/// Une échéance du calendrier fiscal du dossier : l'obligation, la période qu'elle couvre, sa date limite et son état.
/// <see cref="Note"/> est une clé de libellé facultative (<c>Tax.Note.{Note}</c>) qui explique une date ou une dispense.
/// </summary>
public sealed record FiscalDeadlineResponse(
    FiscalObligationDto Obligation,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly DueDate,
    FiscalDeadlineStateDto State,
    DateTimeOffset? FiledAt,
    string? Note = null);

/// <summary>Réglages fiscaux du dossier, utilisés par le calendrier et les brouillons.</summary>
/// <param name="FiscalYearEndMonth">Mois de clôture de l'exercice (12 par défaut).</param>
/// <param name="FiscalYearEndDay">Jour de clôture (31 par défaut).</param>
/// <param name="VatPeriodicity">Rythme de la CA3.</param>
/// <param name="VatDeadlineDay">Jour limite de la TVA (entre le 15 et le 24, indiqué dans l'espace professionnel) ; 15 par défaut, par prudence.</param>
/// <param name="CorporateIncomeTax">Soumis à l'impôt sur les sociétés : acomptes, solde et liasse au calendrier.</param>
/// <param name="Cfe">Redevable de la CFE (15 décembre).</param>
/// <param name="TrackedFrom">Date du premier enregistrement : les échéances antérieures ne passent jamais « en retard ».</param>
/// <param name="Version">Version de concurrence ; 0 tant que les réglages n'ont jamais été enregistrés.</param>
public sealed record FiscalSettingsResponse(
    int FiscalYearEndMonth,
    int FiscalYearEndDay,
    VatPeriodicityDto VatPeriodicity,
    int VatDeadlineDay,
    bool CorporateIncomeTax,
    bool Cfe,
    DateOnly? TrackedFrom,
    long Version);

/// <summary>Enregistrer les réglages fiscaux du dossier.</summary>
public sealed record UpdateFiscalSettingsRequest(
    int FiscalYearEndMonth,
    int FiscalYearEndDay,
    VatPeriodicityDto VatPeriodicity,
    int VatDeadlineDay,
    bool CorporateIncomeTax,
    bool Cfe,
    long Version);

/// <summary>Calendrier fiscal du dossier sur une période, avec les réglages et le régime de TVA utilisés.</summary>
public sealed record FiscalCalendarResponse(
    DateOnly From,
    DateOnly To,
    DateOnly Today,
    VatDeclarationRegimeDto? Regime,
    bool VatFranchise,
    FiscalSettingsResponse Settings,
    IReadOnlyList<FiscalDeadlineResponse> Deadlines);

/// <summary>Marquer une échéance comme déposée (ou revenir dessus) : l'obligation et le premier jour de sa période.</summary>
public sealed record MarkFiscalFilingRequest(FiscalObligationDto Obligation, DateOnly PeriodStart, bool Filed);
