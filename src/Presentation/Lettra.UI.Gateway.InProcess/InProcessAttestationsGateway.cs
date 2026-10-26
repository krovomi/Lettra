using Lettra.Application.Features.Parties;
using Lettra.Contracts.Common;
using Lettra.Contracts.Vigilance;
using Lettra.Domain.Parties;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Mêmes cas d'usage et mêmes permissions que les routes <c>/attestations</c> de l'API.</summary>
internal sealed class InProcessAttestationsGateway(IServiceScopeFactory scopes) : IAttestationsGateway
{
    public Task<OperationResult<SupplierAttestationsResponse>> ListAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new ListAttestationsQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<SupplierAttestationsResponse>> SaveAsync(Guid supplierId, SaveAttestationRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessScope.WriteThenReadAsync(
                sp, new SaveAttestationCommand(dossier, new SupplierId(supplierId), request.ToDraft(), expectedVersion), new ListAttestationsQuery(dossier), ct),
            cancellationToken);
}
