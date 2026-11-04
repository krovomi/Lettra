using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Decisions;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpDecisionsGateway(HttpClient http) : IDecisionsGateway
{
    public Task<OperationResult<DecisionsResponse>> ListAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.GetAsync(HttpCalls.Relative($"api/v1/decisions"), ct), ContractsJsonContext.Default.DecisionsResponse, cancellationToken);

    public Task<OperationResult<DecisionsResponse>> RefreshAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.PostAsync(HttpCalls.Relative($"api/v1/decisions/refresh"), null, ct), ContractsJsonContext.Default.DecisionsResponse, cancellationToken);

    public Task<OperationResult<DecisionsResponse>> AnswerAsync(Guid decisionId, AnswerDecisionRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        VersionedCalls.SendAsync(
            http, HttpMethod.Post, HttpCalls.Relative($"api/v1/decisions/{decisionId}/answer"), expectedVersion,
            JsonContent.Create(request, ContractsJsonContext.Default.AnswerDecisionRequest), ContractsJsonContext.Default.DecisionsResponse, cancellationToken);

    public Task<OperationResult<DecisionsResponse>> SetPolicyAsync(SetDecisionPolicyRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        VersionedCalls.SendAsync(
            http, HttpMethod.Put, HttpCalls.Relative($"api/v1/decisions/policy"), expectedVersion,
            JsonContent.Create(request, ContractsJsonContext.Default.SetDecisionPolicyRequest), ContractsJsonContext.Default.DecisionsResponse, cancellationToken);
}
