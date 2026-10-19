using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Filtre de la liste des encaissements : période et montants (bornes comprises), client, recherche (numéro, client,
/// référence, facture), tri (par défaut la date, la plus récente en tête).
/// </summary>
public sealed record PaymentListQuery(
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? CustomerId = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 50,
    PaymentSortDto Sort = PaymentSortDto.Date,
    SortDirectionDto Direction = SortDirectionDto.Descending,
    decimal? MinAmount = null,
    decimal? MaxAmount = null);

/// <summary>
/// Pages Encaissements et Rapprochement bancaire (vague 6). Lecture : <c>DossierLire</c> ; saisie, contre-passation,
/// import et validation : <c>FactureEmettre</c> ; seuil d'écart : <c>CatalogueGerer</c>. Les relevés viennent de
/// fichiers (CAMT.053, OFX, CSV), jamais d'une connexion à la banque.
/// </summary>
public interface IBankingGateway
{
    Task<OperationResult<PagedResponse<PaymentSummary>>> ListPaymentsAsync(PaymentListQuery query, CancellationToken cancellationToken);

    Task<OperationResult<PaymentResponse>> GetPaymentAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Factures et acomptes validés qui restent dus, par échéance ; d'un client si <paramref name="customerId"/> est donné.</summary>
    Task<OperationResult<IReadOnlyList<OpenInvoiceResponse>>> GetOpenInvoicesAsync(Guid? customerId, CancellationToken cancellationToken);

    Task<OperationResult<PaymentResponse>> RecordPaymentAsync(RecordPaymentRequest request, CancellationToken cancellationToken);

    Task<OperationResult<PaymentResponse>> ReversePaymentAsync(Guid id, ReversePaymentRequest request, CancellationToken cancellationToken);

    Task<OperationResult<BankImportResponse>> ImportStatementAsync(ImportBankStatementRequest request, CancellationToken cancellationToken);

    /// <summary>Démo seulement : un relevé fictif fabriqué à partir des factures dues (refusé hors démonstration).</summary>
    Task<OperationResult<BankImportResponse>> ImportDemoStatementAsync(CancellationToken cancellationToken);

    Task<OperationResult<ReconciliationResponse>> GetReconciliationAsync(CancellationToken cancellationToken);

    Task<OperationResult<ConfirmMatchesResponse>> ConfirmMatchesAsync(ConfirmMatchesRequest request, CancellationToken cancellationToken);

    Task<OperationResult<BankLineResponse>> IgnoreLineAsync(Guid lineId, bool ignored, CancellationToken cancellationToken);

    Task<OperationResult<BankingSettingsResponse>> GetSettingsAsync(CancellationToken cancellationToken);

    Task<OperationResult<BankingSettingsResponse>> UpdateSettingsAsync(UpdateBankingSettingsRequest request, CancellationToken cancellationToken);

    /// <summary>Où en est le rapprochement (dernier import, lignes à rapprocher), pour la vue cabinet.</summary>
    Task<OperationResult<BankReconciliationStatusResponse>> GetStatusAsync(CancellationToken cancellationToken);
}
