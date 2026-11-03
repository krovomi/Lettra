using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.Exports.Application.Insights.Prevention;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Mêmes cas d'usage et mêmes permissions que les routes <c>/insights/prevention</c> de l'API.</summary>
internal sealed class InProcessPreventionGateway(IServiceScopeFactory scopes) : IPreventionGateway
{
    public Task<OperationResult<PreventionRadarResponse>> GetRadarAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetPreventionRadarQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<PreventionSummaryResponse>> GetSummaryAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetPreventionSummaryQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<PreventionSettingsResponse>> UpdateSettingsAsync(UpdatePreventionSettingsRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new UpdatePreventionSettingsCommand(dossier, request), ct), cancellationToken);

    public Task<OperationResult<PreventionLetterResponse>> GetLetterAsync(string? language, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetPreventionLetterQuery(dossier, language), ct), cancellationToken);

    public Task<OperationResult<bool>> SendLetterAsync(SendPreventionLetterRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new SendPreventionLetterCommand(dossier, request), ct), cancellationToken);
}
