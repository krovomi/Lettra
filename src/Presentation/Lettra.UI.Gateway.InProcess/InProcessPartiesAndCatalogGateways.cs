using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Features.Catalog;
using Lettra.Application.Features.Parties;
using Lettra.Application.Mapping;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Directory;
using Lettra.Contracts.Suppliers;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

internal sealed class InProcessSuppliersGateway(IServiceScopeFactory scopes) : ISuppliersGateway
{
    public Task<OperationResult<SupplierResponse>> CreateAsync(CreateSupplierRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessScope.CreateThenReadAsync(sp, request.ToCommand(dossier), id => new GetSupplierQuery(dossier, id), ct),
            cancellationToken);

    public Task<OperationResult<PagedResponse<SupplierSummary>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new ListSuppliersQuery(dossier, page, pageSize), ct), cancellationToken);

    public Task<OperationResult<PagedResponse<SupplierSummary>>> SearchAsync(PartyListQuery query, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessScope.QueryAsync(
                sp, new ListSuppliersQuery(dossier, query.Page, query.PageSize, ListSearch.Normalize(query.Search), query.Sort, query.Direction), ct),
            cancellationToken);

    public Task<OperationResult<SupplierResponse>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetSupplierQuery(dossier, new SupplierId(id)), ct), cancellationToken);

    public Task<OperationResult<SupplierResponse>> UpdateAsync(Guid id, long expectedVersion, CreateSupplierRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.WriteThenReadAsync(
            sp, request.ToUpdateCommand(dossier, new SupplierId(id), expectedVersion), new GetSupplierQuery(dossier, new SupplierId(id)), ct), cancellationToken);

    public Task<OperationResult<bool>> DeleteAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new DeleteSupplierCommand(dossier, new SupplierId(id), expectedVersion), ct), cancellationToken);
}

internal sealed class InProcessCatalogGateway(IServiceScopeFactory scopes) : ICatalogGateway
{
    public Task<OperationResult<CatalogReferenceData>> GetReferenceDataAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetCatalogReferenceDataQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<PagedResponse<CatalogItemSummary>>> ListAsync(CatalogListQuery query, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessScope.QueryAsync(
                sp, new ListCatalogItemsQuery(dossier, query.Search, query.IncludeArchived, query.Page, query.PageSize, query.Sort, query.Direction, query.Kind), ct),
            cancellationToken);

    public Task<OperationResult<CatalogItemResponse>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetCatalogItemQuery(dossier, new CatalogItemId(id)), ct), cancellationToken);

    public Task<OperationResult<CatalogItemResponse>> CreateAsync(SaveCatalogItemRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, async (sp, dossier, ct) =>
        {
            Result<CatalogItemId> created = await sp.GetRequiredService<ICommandDispatcher>()
                .SendAsync(request.ToCreateCommand(dossier), ct).ConfigureAwait(false);
            return created.IsFailure
                ? OperationResult<CatalogItemResponse>.Failure(created.Errors.ToErrorDetails())
                : await InProcessScope.QueryAsync(sp, new GetCatalogItemQuery(dossier, created.Value), ct).ConfigureAwait(false);
        }, cancellationToken);

    public Task<OperationResult<CatalogItemResponse>> UpdateAsync(Guid id, long expectedVersion, SaveCatalogItemRequest request, CancellationToken cancellationToken) =>
        WriteThenReadAsync(id, (dossier, itemId) => request.ToUpdateCommand(dossier, itemId, expectedVersion), cancellationToken);

    public Task<OperationResult<CatalogItemResponse>> SetArchivedAsync(Guid id, long expectedVersion, bool isArchived, CancellationToken cancellationToken) =>
        WriteThenReadAsync(id, (dossier, itemId) => new SetCatalogItemArchivedCommand(dossier, itemId, isArchived, expectedVersion), cancellationToken);

    private Task<OperationResult<CatalogItemResponse>> WriteThenReadAsync(
        Guid id, Func<DossierId, CatalogItemId, ICommand<Result>> command, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, async (sp, dossier, ct) =>
        {
            CatalogItemId itemId = new(id);
            Result written = await sp.GetRequiredService<ICommandDispatcher>().SendAsync(command(dossier, itemId), ct).ConfigureAwait(false);
            if (written.IsSuccess)
            {
                return await InProcessScope.QueryAsync(sp, new GetCatalogItemQuery(dossier, itemId), ct).ConfigureAwait(false);
            }

            // Même contrat que le 412 de l'API : l'article actuel accompagne le refus, pour la fusion.
            if (written.Errors[0].Code == ConcurrencyErrors.VersionMismatchCode)
            {
                OperationResult<CatalogItemResponse> current = await InProcessScope.QueryAsync(sp, new GetCatalogItemQuery(dossier, itemId), ct).ConfigureAwait(false);
                if (current is { IsSuccess: true, Value: { } value })
                {
                    return OperationResult<CatalogItemResponse>.Stale(value);
                }
            }

            return OperationResult<CatalogItemResponse>.Failure(written.Errors.ToErrorDetails());
        }, cancellationToken);
}

internal sealed class InProcessDirectoryGateway(IServiceScopeFactory scopes) : IDirectoryGateway
{
    public Task<OperationResult<CompanySearchResponse>> SearchCompaniesAsync(string query, int maxResults, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new SearchCompaniesQuery(dossier, query, maxResults), ct), cancellationToken);

    public Task<OperationResult<VatNumberCheckResponse>> CheckVatNumberAsync(string vatNumber, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new CheckVatNumberQuery(dossier, vatNumber), ct), cancellationToken);
}
