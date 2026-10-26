using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;
using Lettra.Exports.Application.Banking;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Mêmes cas d'usage et mêmes permissions que les routes <c>/bank/connections</c> de l'API.</summary>
internal sealed class InProcessBankConnectionsGateway(IServiceScopeFactory scopes) : IBankConnectionsGateway
{
    public Task<OperationResult<BankConnectionsResponse>> GetAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetBankConnectionsQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<BankConsentRedirectResponse>> StartAsync(StartBankConnectionRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new StartBankConnectionCommand(dossier, request), ct), cancellationToken);

    public Task<OperationResult<BankConnectionCompletedResponse>> CompleteAsync(Guid connectionId, CompleteBankConnectionRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new CompleteBankConnectionCommand(dossier, connectionId, request), ct), cancellationToken);

    public Task<OperationResult<SyncBankConnectionsResponse>> SyncAsync(SyncBankConnectionsRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new SyncBankConnectionsCommand(dossier, request), ct), cancellationToken);

    public Task<OperationResult<BankConnectionResponse>> RevokeAsync(Guid connectionId, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new RevokeBankConnectionCommand(dossier, connectionId), ct), cancellationToken);
}
