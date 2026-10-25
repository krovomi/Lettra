using Lettra.Application.Features.Parties;
using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;
using Lettra.Domain.Parties;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Mêmes requêtes et mêmes permissions que les routes <c>/suppliers/iban-changes</c> de l'API.</summary>
internal sealed class InProcessIbanChangesGateway(IServiceScopeFactory scopes) : IIbanChangesGateway
{
    public Task<OperationResult<PagedResponse<IbanChangeResponse>>> ListAsync(IbanChangeListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessScope.QueryAsync(
                sp, new ListIbanChangesQuery(dossier, query.Status, ListSearch.Normalize(query.Search), query.Page, query.PageSize, query.Sort, query.Direction), ct),
            cancellationToken);
    }

    public Task<OperationResult<IReadOnlyList<IbanChangeResponse>>> ForSupplierAsync(Guid supplierId, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new ListSupplierIbanChangesQuery(dossier, new SupplierId(supplierId)), ct), cancellationToken);

    public Task<OperationResult<bool>> ConfirmAsync(Guid changeId, ConfirmIbanChangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessScope.SendAsync(
                sp, new ConfirmIbanChangeCommand(dossier, new IbanChangeId(changeId), request.Method, request.Note, request.ExpectedVersion), ct),
            cancellationToken);
    }

    public Task<OperationResult<bool>> RejectAsync(Guid changeId, RejectIbanChangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessScope.SendAsync(sp, new RejectIbanChangeCommand(dossier, new IbanChangeId(changeId), request.Note, request.ExpectedVersion), ct),
            cancellationToken);
    }
}
