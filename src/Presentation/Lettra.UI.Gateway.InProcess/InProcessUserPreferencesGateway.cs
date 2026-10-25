using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Preferences;
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
    public Task<OperationResult<UserPreferencesResponse>> GetAsync(CancellationToken cancellationToken) =>
        InProcessScope.OutsideDossierAsync(scopes, sp => InProcessScope.QueryAsync(sp, new GetUserPreferencesQuery(), cancellationToken));

    public Task<OperationResult<UserPreferencesResponse>> SaveAsync(SaveUserPreferencesRequest request, CancellationToken cancellationToken) =>
        SendAsync(new SaveUserPreferencesCommand(request), cancellationToken);

    public Task<OperationResult<UserPreferencesResponse>> RecordUsageAsync(RecordUsageRequest request, CancellationToken cancellationToken) =>
        SendAsync(new RecordUsageCommand(request), cancellationToken);

    public Task<OperationResult<UserPreferencesResponse>> ForgetUsageAsync(Guid? dossierId, CancellationToken cancellationToken) =>
        SendAsync(new ForgetUsageCommand(dossierId), cancellationToken);

    public Task<OperationResult<UserPreferencesResponse>> ResetAsync(CancellationToken cancellationToken) =>
        SendAsync(new ResetUserPreferencesCommand(), cancellationToken);

    private Task<OperationResult<UserPreferencesResponse>> SendAsync(ICommand<Result<UserPreferencesResponse>> command, CancellationToken cancellationToken) =>
        InProcessScope.OutsideDossierAsync(scopes, sp => InProcessScope.SendAsync(sp, command, cancellationToken));
}
