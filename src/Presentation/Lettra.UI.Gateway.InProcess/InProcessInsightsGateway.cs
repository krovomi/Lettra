using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.Domain.Billing;
using Lettra.Exports.Application.Insights;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Mêmes cas d'usage et mêmes permissions que les routes <c>/insights</c> de l'API.</summary>
internal sealed class InProcessInsightsGateway(IServiceScopeFactory scopes) : IInsightsGateway
{
    public Task<OperationResult<PaymentWeatherResponse>> GetPaymentWeatherAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetPaymentWeatherQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<PracticeAuditResponse>> GetPracticeAuditAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetPracticeAuditQuery(dossier, from, to), ct), cancellationToken);

    public Task<OperationResult<PracticeAuditResponse>> RunPracticeAuditAsync(
        string fileName, byte[] content, DateOnly? from, DateOnly? to, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new RunPracticeAuditQuery(dossier, fileName, content, from, to), ct), cancellationToken);

    public Task<OperationResult<InvoiceProofResponse>> GetInvoiceProofAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetInvoiceProofQuery(dossier, new InvoiceId(invoiceId)), ct), cancellationToken);

    public Task<OperationResult<InvoiceProofCheckResponse>> VerifyInvoiceProofAsync(string code, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new VerifyInvoiceProofQuery(dossier, code), ct), cancellationToken);

    public Task<OperationResult<IReadOnlyList<InvoiceProofKeyResponse>>> ListInvoiceProofKeysAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new ListInvoiceProofKeysQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<DossierAsOfResponse>> GetDossierAsOfAsync(DateTimeOffset at, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetDossierAsOfQuery(dossier, at), ct), cancellationToken);

    public Task<OperationResult<FigureExplanationResponse>> ExplainFigureAsync(ExplainedFigureDto figure, DateOnly? month, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new ExplainFigureQuery(dossier, figure, month), ct), cancellationToken);

    public Task<OperationResult<CallSheetResponse>> GetCallSheetAsync(Guid customerId, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetCallSheetQuery(dossier, customerId), ct), cancellationToken);

    public Task<OperationResult<WhatIfResponse>> SimulateWhatIfAsync(WhatIfRequest scenario, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new SimulateWhatIfQuery(dossier, scenario), ct), cancellationToken);
}
