using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Accounting;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Banking;

/// <summary>Mode de règlement d'un encaissement.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PaymentMethodDto>))]
public enum PaymentMethodDto
{
    Transfer = 1,
    Cheque = 2,
    Card = 3,
    Cash = 4,
    DirectDebit = 5,
}

/// <summary>État d'un encaissement : comptabilisé, ou contre-passé (jamais supprimé).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PaymentStatusDto>))]
public enum PaymentStatusDto
{
    Posted = 1,
    Reversed = 2,
}

/// <summary>Format d'un relevé bancaire importé.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BankStatementFormatDto>))]
public enum BankStatementFormatDto
{
    Camt053 = 1,
    Ofx = 2,
    Csv = 3,
}

/// <summary>État d'une opération du relevé : à rapprocher, rapprochée, écartée.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BankLineStatusDto>))]
public enum BankLineStatusDto
{
    Pending = 1,
    Matched = 2,
    Ignored = 3,
}

/// <summary>Règle qui a produit une proposition de rapprochement, de la plus sûre à la moins sûre.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MatchRuleDto>))]
public enum MatchRuleDto
{
    InvoiceNumber = 1,
    AmountAndName = 2,
    AmountAndIban = 3,
    SumOfInvoices = 4,
}

/// <summary>La part d'un encaissement imputée sur une facture ou un acompte.</summary>
public sealed record PaymentAllocationRequest(Guid InvoiceId, decimal Amount);

/// <summary>
/// Saisie d'un encaissement : un client, une date, le montant reçu, le mode, et son imputation sur une ou plusieurs de
/// ses factures (total ou partiel). Un écart sous le seuil du dossier est passé en écart de règlement (658 ou 758).
/// </summary>
public sealed record RecordPaymentRequest
{
    public Guid CustomerId { get; set; }

    public DateOnly Date { get; set; }

    [Range(0.01d, 999_999_999.99d, ErrorMessage = ErrorCodes.Payment.AmountInvalid)]
    public decimal Amount { get; set; }

    public PaymentMethodDto Method { get; set; } = PaymentMethodDto.Transfer;

    [StringLength(140, ErrorMessage = ErrorCodes.Payment.ReferenceTooLong)]
    public string? Reference { get; set; }

    public IReadOnlyList<PaymentAllocationRequest> Allocations { get; set; } = [];

    /// <summary>Opération du relevé bancaire d'où vient l'encaissement, s'il est saisi depuis le rapprochement.</summary>
    public Guid? BankLineId { get; set; }
}

/// <summary>Contre-passation d'un encaissement : le motif est obligatoire ; la date par défaut est celle du jour.</summary>
public sealed record ReversePaymentRequest
{
    [StringLength(500, ErrorMessage = ErrorCodes.Payment.ReasonTooLong)]
    public string? Reason { get; set; }

    public DateOnly? Date { get; set; }
}

public sealed record PaymentAllocationResponse(Guid InvoiceId, string InvoiceNumber, decimal Amount);

/// <summary>
/// Un encaissement : son imputation, son écriture au journal de banque (et celle de sa contre-passation), le code de
/// lettrage du compte client quand ses factures sont soldées.
/// </summary>
public sealed record PaymentResponse(
    Guid Id,
    long Version,
    string Number,
    PaymentStatusDto Status,
    Guid CustomerId,
    string CustomerName,
    DateOnly Date,
    decimal Amount,
    PaymentMethodDto Method,
    string? Reference,
    Guid? BankLineId,
    IReadOnlyList<PaymentAllocationResponse> Allocations,
    decimal AllocatedAmount,
    decimal WriteOff,
    string? LetteringCode,
    IReadOnlyList<JournalLineResponse> EntryLines,
    IReadOnlyList<JournalLineResponse> ReversalLines,
    DateOnly? ReversalDate,
    string? ReversalReason,
    DateTimeOffset CreatedAt,
    int PlatformReports = 0);

/// <summary>Ligne de la liste des encaissements.</summary>
public sealed record PaymentSummary(
    Guid Id,
    string Number,
    PaymentStatusDto Status,
    Guid CustomerId,
    string CustomerName,
    DateOnly Date,
    decimal Amount,
    PaymentMethodDto Method,
    string? Reference,
    IReadOnlyList<string> InvoiceNumbers,
    string? LetteringCode);

/// <summary>Une facture ou un acompte validé qui reste dû, pour la saisie d'un encaissement et le rapprochement.</summary>
public sealed record OpenInvoiceResponse(
    Guid InvoiceId,
    InvoiceKindDto Kind,
    string Number,
    Guid CustomerId,
    string CustomerName,
    DateOnly IssueDate,
    DateOnly? DueDate,
    decimal TotalInclTax,
    decimal CreditedInclTax,
    decimal PaidInclTax,
    decimal Outstanding,
    int DaysOverdue);

/// <summary>
/// Colonnes d'un relevé CSV, numérotées à partir de 1. Sans réglage, Lettra reconnaît l'en-tête (Date, Libellé,
/// Montant, Débit, Crédit, Référence…) et le séparateur. Un montant unique est signé ; sinon Débit et Crédit.
/// </summary>
public sealed record CsvImportOptions
{
    /// <summary><c>;</c>, <c>,</c> ou une tabulation ; vide pour le deviner.</summary>
    [StringLength(1, ErrorMessage = ErrorCodes.BankStatement.CsvColumnsInvalid)]
    public string? Separator { get; set; }

    public bool HasHeader { get; set; } = true;

    public int? DateColumn { get; set; }

    public int? LabelColumn { get; set; }

    public int? AmountColumn { get; set; }

    public int? DebitColumn { get; set; }

    public int? CreditColumn { get; set; }

    public int? ReferenceColumn { get; set; }

    public int? CounterpartyColumn { get; set; }

    /// <summary>Format de date (<c>dd/MM/yyyy</c> par défaut, ISO reconnu aussi).</summary>
    [StringLength(20, ErrorMessage = ErrorCodes.BankStatement.CsvColumnsInvalid)]
    public string? DateFormat { get; set; }
}

/// <summary>Import d'un relevé bancaire (CAMT.053, OFX ou CSV) : le fichier en base 64 et, pour un CSV, ses colonnes.</summary>
public sealed record ImportBankStatementRequest
{
    [StringLength(200, ErrorMessage = ErrorCodes.BankStatement.FileRequired)]
    public string FileName { get; set; } = string.Empty;

    public string ContentBase64 { get; set; } = string.Empty;

    public CsvImportOptions? Csv { get; set; }
}

/// <summary>Compte rendu d'un import : opérations ajoutées, doublons écartés, propositions trouvées.</summary>
public sealed record BankImportResponse(
    Guid ImportId,
    BankStatementFormatDto Format,
    string? AccountDisplay,
    DateOnly? From,
    DateOnly? To,
    int Imported,
    int Duplicates,
    int Proposals);

public sealed record ProposedAllocationResponse(Guid InvoiceId, string Number, decimal Amount);

/// <summary>Une proposition de rapprochement et sa raison lisible.</summary>
public sealed record MatchProposalResponse(
    MatchRuleDto Rule,
    Guid CustomerId,
    string CustomerName,
    IReadOnlyList<ProposedAllocationResponse> Allocations,
    decimal WriteOff,
    string Evidence);

/// <summary>Une opération du relevé, avec sa proposition quand une règle la reconnaît.</summary>
public sealed record BankLineResponse(
    Guid Id,
    long Version,
    DateOnly BookingDate,
    decimal Amount,
    string Label,
    string? Reference,
    string? CounterpartyName,
    string? CounterpartyIban,
    BankLineStatusDto Status,
    Guid? PaymentId,
    string? PaymentNumber,
    MatchProposalResponse? Proposal);

/// <summary>Un import de relevé, pour l'historique de l'écran.</summary>
public sealed record BankImportSummary(
    Guid Id,
    string FileName,
    BankStatementFormatDto Format,
    string? AccountDisplay,
    DateOnly? From,
    DateOnly? To,
    int Imported,
    int Duplicates,
    DateTimeOffset ImportedAt);

/// <summary>L'écran de rapprochement : opérations (à rapprocher d'abord), compteurs, seuil et derniers imports.</summary>
public sealed record ReconciliationResponse(
    IReadOnlyList<BankLineResponse> Lines,
    int Pending,
    int Matched,
    int Ignored,
    int Proposals,
    decimal WriteOffThreshold,
    IReadOnlyList<BankImportSummary> Imports);

/// <summary>Une opération à valider : le client et l'imputation (ceux de la proposition, ou corrigés).</summary>
public sealed record ConfirmMatchItem(Guid BankLineId, Guid CustomerId, IReadOnlyList<PaymentAllocationRequest> Allocations, PaymentMethodDto Method = PaymentMethodDto.Transfer);

/// <summary>Validation en un clic ou en masse : chaque opération devient un encaissement, indépendamment des autres.</summary>
public sealed record ConfirmMatchesRequest
{
    public IReadOnlyList<ConfirmMatchItem> Items { get; set; } = [];
}

public sealed record ConfirmMatchOutcome(Guid BankLineId, Guid? PaymentId, string? PaymentNumber, IReadOnlyList<ErrorDetail> Errors);

public sealed record ConfirmMatchesResponse(int Confirmed, int Failed, IReadOnlyList<ConfirmMatchOutcome> Outcomes);

/// <summary>Écarter une opération du relevé (frais, virement interne…) ou la remettre à rapprocher.</summary>
public sealed record IgnoreBankLineRequest
{
    public bool Ignored { get; set; } = true;
}

public sealed record BankingSettingsResponse(decimal WriteOffThreshold, decimal MaxWriteOffThreshold, long Version);

public sealed record UpdateBankingSettingsRequest
{
    [Range(0d, 100d, ErrorMessage = ErrorCodes.BankingSettings.ThresholdInvalid)]
    public decimal WriteOffThreshold { get; set; }
}

/// <summary>
/// Où en est le rapprochement bancaire du dossier, pour la vue cabinet : dernier import, opérations à rapprocher et la
/// plus ancienne d'entre elles. Compteurs seulement, aucun montant.
/// </summary>
public sealed record BankReconciliationStatusResponse(
    DateTimeOffset? LastImportAt,
    DateOnly? LastStatementDate,
    int PendingLines,
    DateOnly? OldestPendingDate,
    DateTimeOffset? LastMatchedAt);
