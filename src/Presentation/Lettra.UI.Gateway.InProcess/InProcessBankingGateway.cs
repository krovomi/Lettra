using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;
using Lettra.Exports.Application.Banking;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Mêmes cas d'usage et mêmes permissions que les routes <c>/payments</c> et <c>/bank</c> de l'API.</summary>
internal sealed class InProcessBankingGateway(IServiceScopeFactory scopes) : IBankingGateway
{
    public Task<OperationResult<PagedResponse<PaymentSummary>>> ListPaymentsAsync(PaymentListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new ListPaymentsQuery(
            dossier, query.From, query.To, query.CustomerId, ListSearch.Normalize(query.Search), query.Page, query.PageSize,
            query.Sort, query.Direction, query.MinAmount, query.MaxAmount), ct), cancellationToken);
    }

    public Task<OperationResult<PaymentResponse>> GetPaymentAsync(Guid id, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetPaymentQuery(dossier, id), ct), cancellationToken);

    public Task<OperationResult<IReadOnlyList<OpenInvoiceResponse>>> GetOpenInvoicesAsync(Guid? customerId, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetOpenInvoicesQuery(dossier, customerId), ct), cancellationToken);

    public Task<OperationResult<PaymentResponse>> RecordPaymentAsync(RecordPaymentRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new RecordPaymentCommand(dossier, request), ct), cancellationToken);

    public Task<OperationResult<PaymentResponse>> ReversePaymentAsync(Guid id, ReversePaymentRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new ReversePaymentCommand(dossier, id, request), ct), cancellationToken);

    public Task<OperationResult<BankImportResponse>> ImportStatementAsync(ImportBankStatementRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new ImportBankStatementCommand(dossier, request), ct), cancellationToken);

    // Sur l'appareil (mode 1), le relevé de démonstration est permis : le dossier local est celui de l'utilisateur, l'écran
    // ne propose le bouton que lorsque le jeu de démonstration est disponible.
    public Task<OperationResult<BankImportResponse>> ImportDemoStatementAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new ImportDemoBankStatementCommand(dossier), ct), cancellationToken);

    public Task<OperationResult<ReconciliationResponse>> GetReconciliationAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetReconciliationQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<ConfirmMatchesResponse>> ConfirmMatchesAsync(ConfirmMatchesRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new ConfirmMatchesCommand(dossier, request), ct), cancellationToken);

    public Task<OperationResult<BankLineResponse>> IgnoreLineAsync(Guid lineId, bool ignored, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new IgnoreBankLineCommand(dossier, lineId, new IgnoreBankLineRequest { Ignored = ignored }), ct), cancellationToken);

    public Task<OperationResult<BankingSettingsResponse>> GetSettingsAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetBankingSettingsQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<BankingSettingsResponse>> UpdateSettingsAsync(UpdateBankingSettingsRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new UpdateBankingSettingsCommand(dossier, request), ct), cancellationToken);

    public Task<OperationResult<BankReconciliationStatusResponse>> GetStatusAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetBankReconciliationStatusQuery(dossier), ct), cancellationToken);
}
