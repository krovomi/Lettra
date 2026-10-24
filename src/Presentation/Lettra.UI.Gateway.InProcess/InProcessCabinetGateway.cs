using Lettra.Application.Features.Cabinet;
using Lettra.Application.Features.Dossiers;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Common;
using Lettra.Contracts.Dossiers;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>
/// Mêmes cas d'usage que les routes <c>/dossiers/overview</c> et <c>/review</c> de l'API. Le tableau et le sélecteur
/// sont hors de tout dossier ; la revue s'exécute dans le dossier ouvert.
/// </summary>
internal sealed class InProcessCabinetGateway(IServiceScopeFactory scopes) : ICabinetGateway
{
    public Task<OperationResult<IReadOnlyList<DossierSummary>>> ListDossiersAsync(CancellationToken cancellationToken) =>
        InProcessScope.OutsideDossierAsync(scopes, sp => InProcessScope.QueryAsync(sp, new ListAccessibleDossiersQuery(), cancellationToken));

    public Task<OperationResult<CabinetOverviewResponse>> GetOverviewAsync(CancellationToken cancellationToken) =>
        InProcessScope.OutsideDossierAsync(scopes, sp => InProcessScope.QueryAsync(sp, new GetCabinetOverviewQuery(), cancellationToken));

    public Task<OperationResult<CabinetOverviewRefreshResponse>> RefreshOverviewAsync(Guid? dossierId, CancellationToken cancellationToken) =>
        InProcessScope.OutsideDossierAsync(
            scopes, sp => InProcessScope.SendAsync(sp, new RefreshCabinetOverviewCommand(dossierId is Guid id ? [id] : null), cancellationToken));

    public Task<OperationResult<DossierReviewResponse>> GetReviewAsync(string period, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetDossierReviewQuery(dossier, period), ct), cancellationToken);

    public Task<OperationResult<DossierReviewResponse>> SetReviewItemAsync(string period, string item, SetDossierReviewItemRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessScope.SendAsync(sp, new SetDossierReviewItemCommand(dossier, period, item, request.Checked, request.Note), ct),
            cancellationToken);
    }
}
