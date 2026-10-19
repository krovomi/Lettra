using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Preferences;
using Lettra.Application.Mapping;
using Lettra.Contracts.Common;
using Lettra.Contracts.Preferences;
using Lettra.Domain.Common;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>
/// Mêmes cas d'usage que <c>/api/v1/me/preferences</c>, hors de tout dossier. En mode 1 (poste isolé, sans base Catalog)
/// chaque appel rend <c>Preferences.Unavailable</c> : l'interface garde les préférences sur l'appareil.
/// </summary>
internal sealed class InProcessUserPreferencesGateway(IServiceScopeFactory scopes) : IUserPreferencesGateway
{
    public async Task<OperationResult<UserPreferencesResponse>> GetAsync(CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            return await InProcessScope.QueryAsync(scope.ServiceProvider, new GetUserPreferencesQuery(), cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<OperationResult<UserPreferencesResponse>> SaveAsync(SaveUserPreferencesRequest request, CancellationToken cancellationToken) =>
        SendAsync(new SaveUserPreferencesCommand(request), cancellationToken);

    public Task<OperationResult<UserPreferencesResponse>> RecordUsageAsync(RecordUsageRequest request, CancellationToken cancellationToken) =>
        SendAsync(new RecordUsageCommand(request), cancellationToken);

    public Task<OperationResult<UserPreferencesResponse>> ForgetUsageAsync(Guid? dossierId, CancellationToken cancellationToken) =>
        SendAsync(new ForgetUsageCommand(dossierId), cancellationToken);

    public Task<OperationResult<UserPreferencesResponse>> ResetAsync(CancellationToken cancellationToken) =>
        SendAsync(new ResetUserPreferencesCommand(), cancellationToken);

    private async Task<OperationResult<UserPreferencesResponse>> SendAsync(ICommand<Result<UserPreferencesResponse>> command, CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            Result<UserPreferencesResponse> result = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(command, cancellationToken).ConfigureAwait(false);
            return result.ToOperationResult();
        }
    }
}
