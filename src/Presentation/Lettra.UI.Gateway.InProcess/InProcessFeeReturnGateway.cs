using Lettra.Contracts.Common;
using Lettra.Contracts.Tax;
using Lettra.Domain.Parties;
using Lettra.Exports.Application.Tax;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Mêmes cas d'usage et mêmes permissions que les routes <c>/tax/fees</c> de l'API.</summary>
internal sealed class InProcessFeeReturnGateway(IServiceScopeFactory scopes) : IFeeReturnGateway
{
    public Task<OperationResult<FeeReturnResponse>> GetAsync(DateOnly? date, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetFeeReturnQuery(dossier, date), ct), cancellationToken);

    public Task<OperationResult<FeeReturnResponse>> SaveAsync(Guid supplierId, SaveFeeBeneficiaryRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessScope.WriteThenReadAsync(
                sp, new SaveFeeBeneficiaryCommand(dossier, new SupplierId(supplierId), request, expectedVersion), new GetFeeReturnQuery(dossier, request.FiscalYearStart), ct),
            cancellationToken);

    public Task<OperationResult<FeeReturnResponse>> RemoveAsync(Guid supplierId, DateOnly fiscalYearStart, long expectedVersion, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessScope.WriteThenReadAsync(
                sp, new RemoveFeeBeneficiaryCommand(dossier, new SupplierId(supplierId), expectedVersion), new GetFeeReturnQuery(dossier, fiscalYearStart), ct),
            cancellationToken);
}
