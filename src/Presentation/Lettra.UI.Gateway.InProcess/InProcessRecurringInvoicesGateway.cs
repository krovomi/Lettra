using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Billing;
using Lettra.Application.Mapping;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Mêmes cas d'usage et mêmes permissions que les routes <c>/recurring-invoices</c> de l'API.</summary>
internal sealed class InProcessRecurringInvoicesGateway(IServiceScopeFactory scopes) : IRecurringInvoicesGateway
{
    public Task<OperationResult<PagedResponse<RecurringInvoiceSummary>>> ListAsync(RecurringInvoiceListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessScope.QueryAsync(
                sp,
                new ListRecurringInvoicesQuery(
                    dossier, query.Search, query.CustomerId, query.Active, query.DueOnly, query.Sort, query.Descending, query.Page, query.PageSize),
                ct),
            cancellationToken);
    }

    public Task<OperationResult<RecurringInvoiceResponse>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetRecurringInvoiceQuery(dossier, new RecurringInvoiceId(id)), ct), cancellationToken);

    public Task<OperationResult<RecurringInvoiceResponse>> CreateAsync(SaveRecurringInvoiceRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessCommands.CreateThenReadAsync(
                sp, new CreateRecurringInvoiceCommand(dossier, request), id => new GetRecurringInvoiceQuery(dossier, id), ct),
            cancellationToken);

    public Task<OperationResult<RecurringInvoiceResponse>> UpdateAsync(Guid id, SaveRecurringInvoiceRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessCommands.SendThenReadAsync(
                sp,
                new UpdateRecurringInvoiceCommand(dossier, new RecurringInvoiceId(id), request, expectedVersion),
                new GetRecurringInvoiceQuery(dossier, new RecurringInvoiceId(id)),
                ct),
            cancellationToken);

    public Task<OperationResult<RecurringInvoiceResponse>> SetActiveAsync(Guid id, bool isActive, long? expectedVersion, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessCommands.SendThenReadAsync(
                sp,
                new SetRecurringInvoiceActiveCommand(dossier, new RecurringInvoiceId(id), isActive, expectedVersion),
                new GetRecurringInvoiceQuery(dossier, new RecurringInvoiceId(id)),
                ct),
            cancellationToken);

    public Task<OperationResult<bool>> DeleteAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes, (sp, dossier, ct) => InProcessCommands.SendAsync(sp, new DeleteRecurringInvoiceCommand(dossier, new RecurringInvoiceId(id), expectedVersion), ct), cancellationToken);

    public Task<OperationResult<RecurringRunResponse>> RunAsync(Guid? id, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            async (sp, dossier, ct) =>
            {
                Result<RecurringRunResponse> run = await sp.GetRequiredService<ICommandDispatcher>().SendAsync(
                    new GenerateRecurringInvoicesCommand(dossier, id is { } one ? new RecurringInvoiceId(one) : null), ct).ConfigureAwait(false);
                return run.ToOperationResult();
            },
            cancellationToken);
}
