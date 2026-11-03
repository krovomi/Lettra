using Lettra.Application.Features.Decisions;
using Lettra.Contracts.Common;
using Lettra.Contracts.Decisions;
using Lettra.Domain.Decisions;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Mêmes cas d'usage et mêmes permissions que les routes <c>/decisions</c> de l'API.</summary>
internal sealed class InProcessDecisionsGateway(IServiceScopeFactory scopes) : IDecisionsGateway
{
    public Task<OperationResult<DecisionsResponse>> ListAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new ListDecisionsQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<DecisionsResponse>> RefreshAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes, (sp, dossier, ct) => InProcessScope.WriteThenReadAsync(sp, new RefreshDecisionsCommand(dossier), new ListDecisionsQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<DecisionsResponse>> AnswerAsync(Guid decisionId, AnswerDecisionRequest request, long? expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessScope.WriteThenReadAsync(
                sp, new AnswerDecisionCommand(dossier, new DecisionId(decisionId), request.Choice, request.SnoozeUntil, expectedVersion), new ListDecisionsQuery(dossier), ct),
            cancellationToken);
    }

    public Task<OperationResult<DecisionsResponse>> SetPolicyAsync(SetDecisionPolicyRequest request, long? expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessScope.WriteThenReadAsync(sp, new SetDecisionPolicyCommand(dossier, request.Level, expectedVersion), new ListDecisionsQuery(dossier), ct),
            cancellationToken);
    }
}
