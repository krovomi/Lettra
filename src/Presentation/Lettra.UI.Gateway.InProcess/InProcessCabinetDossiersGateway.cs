using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Dossiers;
using Lettra.Contracts.Common;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Mêmes cas d'usage que les routes des dossiers du cabinet, hors de tout dossier ouvert.</summary>
internal sealed class InProcessCabinetDossiersGateway(IServiceScopeFactory scopes) : ICabinetDossiersGateway
{
    public Task<OperationResult<IReadOnlyList<CabinetDossierResponse>>> ListAsync(Guid cabinetId, CancellationToken cancellationToken) =>
        QueryAsync(new ListCabinetDossiersQuery(new OrganisationId(cabinetId)), cancellationToken);

    public Task<OperationResult<DossierSummary>> CreateAsync(Guid cabinetId, CreateDossierRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync(new CreateDossierCommand(new OrganisationId(cabinetId), request.Name), cancellationToken);
    }

    public Task<OperationResult<DossierErasureResponse>> ScheduleErasureAsync(Guid cabinetId, Guid dossierId, CancellationToken cancellationToken) =>
        SendAsync(new ScheduleDossierErasureCommand(new OrganisationId(cabinetId), new DossierId(dossierId)), cancellationToken);

    public Task<OperationResult<DossierSummary>> CancelErasureAsync(Guid cabinetId, Guid dossierId, CancellationToken cancellationToken) =>
        SendAsync(new CancelDossierErasureCommand(new OrganisationId(cabinetId), new DossierId(dossierId)), cancellationToken);

    private Task<OperationResult<T>> QueryAsync<T>(IQuery<Result<T>> query, CancellationToken cancellationToken) =>
        InProcessScope.OutsideDossierAsync(scopes, sp => InProcessScope.QueryAsync(sp, query, cancellationToken));

    private Task<OperationResult<T>> SendAsync<T>(ICommand<Result<T>> command, CancellationToken cancellationToken) =>
        InProcessScope.OutsideDossierAsync(scopes, sp => InProcessScope.SendAsync(sp, command, cancellationToken));
}
