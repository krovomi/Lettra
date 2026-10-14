using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Identity;
using Lettra.Application.Mapping;
using Lettra.Contracts.Common;
using Lettra.Contracts.Identity;
using Lettra.Domain.Common;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Session du poste isolé : les cas d'usage du compte local, par le même pipeline que le reste.</summary>
internal sealed class InProcessSessionGateway(IServiceScopeFactory scopes) : ISessionGateway
{
    public string? SignInPath => null;

    public Task<OperationResult<SessionResponse>> GetAsync(CancellationToken cancellationToken) =>
        QueryAsync(new GetLocalSessionQuery(), cancellationToken);

    public Task<OperationResult<SessionResponse>> CreateLocalAccountAsync(CreateLocalAccountRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync(new CreateLocalAccountCommand(request.DisplayName, request.Passphrase), cancellationToken);
    }

    public Task<OperationResult<SessionResponse>> UnlockAsync(UnlockLocalSessionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync(new OpenLocalSessionCommand(request.Passphrase), cancellationToken);
    }

    public Task<OperationResult<SessionResponse>> LockAsync(CancellationToken cancellationToken) =>
        SendAsync(new CloseLocalSessionCommand(), cancellationToken);

    private async Task<OperationResult<SessionResponse>> SendAsync(ICommand<Result<SessionResponse>> command, CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            Result<SessionResponse> result = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                .SendAsync(command, cancellationToken).ConfigureAwait(false);
            return result.ToOperationResult();
        }
    }

    private async Task<OperationResult<SessionResponse>> QueryAsync(IQuery<Result<SessionResponse>> query, CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            Result<SessionResponse> result = await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>()
                .QueryAsync(query, cancellationToken).ConfigureAwait(false);
            return result.ToOperationResult();
        }
    }
}
