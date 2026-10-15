using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Pricing;
using Lettra.Application.Mapping;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Pricing;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Pricing;
using Lettra.Domain.SharedKernel;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Une commande puis la relecture de la pièce, dans le même scope (comme l'API).</summary>
internal static class InProcessCommands
{
    public static async Task<OperationResult<T>> SendThenReadAsync<T>(
        IServiceProvider services, ICommand<Result> command, IQuery<Result<T>> read, CancellationToken cancellationToken)
    {
        Result done = await services.GetRequiredService<ICommandDispatcher>().SendAsync(command, cancellationToken).ConfigureAwait(false);
        if (done.IsSuccess)
        {
            return await InProcessScope.QueryAsync(services, read, cancellationToken).ConfigureAwait(false);
        }

        // Même contrat que le 412 de l'API : la pièce actuelle accompagne le refus, pour la fusion.
        if (done.Errors[0].Code == ConcurrencyErrors.VersionMismatchCode)
        {
            OperationResult<T> current = await InProcessScope.QueryAsync(services, read, cancellationToken).ConfigureAwait(false);
            if (current is { IsSuccess: true, Value: { } value })
            {
                return OperationResult<T>.Stale(value);
            }
        }

        return OperationResult<T>.Failure(done.Errors.ToErrorDetails());
    }

    public static async Task<OperationResult<T>> CreateThenReadAsync<TId, T>(
        IServiceProvider services, ICommand<Result<TId>> command, Func<TId, IQuery<Result<T>>> read, CancellationToken cancellationToken)
    {
        Result<TId> created = await services.GetRequiredService<ICommandDispatcher>().SendAsync(command, cancellationToken).ConfigureAwait(false);
        return created.IsFailure
            ? OperationResult<T>.Failure(created.Errors.ToErrorDetails())
            : await InProcessScope.QueryAsync(services, read(created.Value), cancellationToken).ConfigureAwait(false);
    }

    public static async Task<OperationResult<bool>> SendAsync(IServiceProvider services, ICommand<Result> command, CancellationToken cancellationToken)
    {
        Result done = await services.GetRequiredService<ICommandDispatcher>().SendAsync(command, cancellationToken).ConfigureAwait(false);
        return done.IsFailure ? OperationResult<bool>.Failure(done.Errors.ToErrorDetails()) : OperationResult<bool>.Success(true);
    }
}

internal sealed class InProcessQuotesGateway(IServiceScopeFactory scopes) : IQuotesGateway
{
    public Task<OperationResult<PagedResponse<QuoteSummary>>> ListAsync(BillingListQuery query, QuoteStatusDto? status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessScope.QueryAsync(
                sp, new ListQuotesQuery(dossier, query.Search, query.CustomerId, query.From, query.To, status, query.Page, query.PageSize), ct),
            cancellationToken);
    }

    public Task<OperationResult<QuoteResponse>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetQuoteQuery(dossier, new QuoteId(id)), ct), cancellationToken);

    public Task<OperationResult<QuoteResponse>> CreateAsync(SaveQuoteRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessCommands.CreateThenReadAsync(sp, new CreateQuoteCommand(dossier, request), id => new GetQuoteQuery(dossier, id), ct),
            cancellationToken);

    public Task<OperationResult<QuoteResponse>> UpdateAsync(Guid id, SaveQuoteRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessCommands.SendThenReadAsync(
                sp, new UpdateQuoteCommand(dossier, new QuoteId(id), request, expectedVersion), new GetQuoteQuery(dossier, new QuoteId(id)), ct),
            cancellationToken);

    public Task<OperationResult<bool>> DeleteAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes, (sp, dossier, ct) => InProcessCommands.SendAsync(sp, new DeleteQuoteCommand(dossier, new QuoteId(id), expectedVersion), ct), cancellationToken);

    public Task<OperationResult<ComplianceReportResponse>> CheckAsync(Guid id, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new CheckQuoteQuery(dossier, new QuoteId(id)), ct), cancellationToken);

    public Task<OperationResult<QuoteResponse>> ValidateAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessCommands.SendThenReadAsync(
                sp, new ValidateQuoteCommand(dossier, new QuoteId(id), expectedVersion), new GetQuoteQuery(dossier, new QuoteId(id)), ct),
            cancellationToken);

    public Task<OperationResult<QuoteResponse>> SetOutcomeAsync(Guid id, bool accepted, long? expectedVersion, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessCommands.SendThenReadAsync(
                sp, new SetQuoteOutcomeCommand(dossier, new QuoteId(id), accepted, expectedVersion), new GetQuoteQuery(dossier, new QuoteId(id)), ct),
            cancellationToken);

    public Task<OperationResult<InvoiceResponse>> CreateDownPaymentAsync(Guid id, CreateDownPaymentRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessCommands.CreateThenReadAsync(
                sp, new CreateDownPaymentCommand(dossier, new QuoteId(id), request), invoice => new GetInvoiceQuery(dossier, invoice), ct),
            cancellationToken);

    public Task<OperationResult<InvoiceResponse>> CreateInvoiceAsync(Guid id, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessCommands.CreateThenReadAsync(
                sp, new CreateFinalInvoiceCommand(dossier, new QuoteId(id)), invoice => new GetInvoiceQuery(dossier, invoice), ct),
            cancellationToken);
}

internal sealed class InProcessInvoicesGateway(IServiceScopeFactory scopes) : IInvoicesGateway
{
    public Task<OperationResult<PagedResponse<InvoiceSummary>>> ListAsync(BillingListQuery query, InvoiceKindDto? kind, InvoiceStatusDto? status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessScope.QueryAsync(
                sp, new ListInvoicesQuery(dossier, query.Search, query.CustomerId, query.From, query.To, kind, status, query.Page, query.PageSize), ct),
            cancellationToken);
    }

    public Task<OperationResult<InvoiceResponse>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetInvoiceQuery(dossier, new InvoiceId(id)), ct), cancellationToken);

    public Task<OperationResult<InvoiceResponse>> CreateAsync(SaveInvoiceRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessCommands.CreateThenReadAsync(sp, new CreateInvoiceCommand(dossier, request), id => new GetInvoiceQuery(dossier, id), ct),
            cancellationToken);

    public Task<OperationResult<InvoiceResponse>> UpdateAsync(Guid id, SaveInvoiceRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        Lifecycle(id, (dossier, invoice) => new UpdateInvoiceCommand(dossier, invoice, request, expectedVersion), cancellationToken);

    public Task<OperationResult<bool>> DeleteAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes, (sp, dossier, ct) => InProcessCommands.SendAsync(sp, new DeleteInvoiceCommand(dossier, new InvoiceId(id), expectedVersion), ct), cancellationToken);

    public Task<OperationResult<ComplianceReportResponse>> CheckAsync(Guid id, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new CheckInvoiceQuery(dossier, new InvoiceId(id)), ct), cancellationToken);

    public Task<OperationResult<InvoiceResponse>> ValidateAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken) =>
        Lifecycle(id, (dossier, invoice) => new ValidateInvoiceCommand(dossier, invoice, expectedVersion), cancellationToken);

    public Task<OperationResult<InvoiceResponse>> IssueAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken) =>
        Lifecycle(id, (dossier, invoice) => new IssueInvoiceCommand(dossier, invoice, expectedVersion), cancellationToken);

    public Task<OperationResult<InvoiceResponse>> CreateCreditNoteAsync(Guid id, CreateCreditNoteRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessCommands.CreateThenReadAsync(
                sp, new CreateCreditNoteCommand(dossier, new InvoiceId(id), request), credit => new GetInvoiceQuery(dossier, credit), ct),
            cancellationToken);

    private Task<OperationResult<InvoiceResponse>> Lifecycle(Guid id, Func<DossierId, InvoiceId, ICommand<Result>> command, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessCommands.SendThenReadAsync(sp, command(dossier, new InvoiceId(id)), new GetInvoiceQuery(dossier, new InvoiceId(id)), ct),
            cancellationToken);
}

internal sealed class InProcessBillingSettingsGateway(IServiceScopeFactory scopes) : IBillingSettingsGateway
{
    public Task<OperationResult<BillingReferenceData>> GetReferenceDataAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetBillingReferenceDataQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<CompanyProfileResponse>> GetCompanyProfileAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetCompanyProfileQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<CompanyProfileResponse>> SaveCompanyProfileAsync(SaveCompanyProfileRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessCommands.SendThenReadAsync(
                sp, new SaveCompanyProfileCommand(dossier, request.ToDraft(), expectedVersion), new GetCompanyProfileQuery(dossier), ct),
            cancellationToken);
}

internal sealed class InProcessPriceListsGateway(IServiceScopeFactory scopes) : IPriceListsGateway
{
    public Task<OperationResult<PagedResponse<PriceListSummary>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new ListPriceListsQuery(dossier, page, pageSize), ct), cancellationToken);

    public Task<OperationResult<PriceListResponse>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetPriceListQuery(dossier, new PriceListId(id)), ct), cancellationToken);

    public Task<OperationResult<PriceListResponse>> CreateAsync(SavePriceListRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessCommands.CreateThenReadAsync(sp, new CreatePriceListCommand(dossier, request), id => new GetPriceListQuery(dossier, id), ct),
            cancellationToken);

    public Task<OperationResult<PriceListResponse>> UpdateAsync(Guid id, SavePriceListRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessCommands.SendThenReadAsync(
                sp, new UpdatePriceListCommand(dossier, new PriceListId(id), request, expectedVersion), new GetPriceListQuery(dossier, new PriceListId(id)), ct),
            cancellationToken);

    public Task<OperationResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes, (sp, dossier, ct) => InProcessCommands.SendAsync(sp, new DeletePriceListCommand(dossier, new PriceListId(id)), ct), cancellationToken);

    public Task<OperationResult<IReadOnlyList<ResolvedPrice>>> ResolveAsync(Guid customerId, IReadOnlyCollection<Guid> catalogItemIds, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new ResolvePricesQuery(dossier, customerId, catalogItemIds), ct), cancellationToken);
}
