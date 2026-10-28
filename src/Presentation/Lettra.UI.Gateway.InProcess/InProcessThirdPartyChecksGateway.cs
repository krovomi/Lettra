using Lettra.Application.Features.Parties;
using Lettra.Contracts.Common;
using Lettra.Contracts.Vigilance;
using Lettra.Domain.Vigilance;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Mêmes cas d'usage et mêmes permissions que les routes <c>/third-party-checks</c> de l'API.</summary>
internal sealed class InProcessThirdPartyChecksGateway(IServiceScopeFactory scopes) : IThirdPartyChecksGateway
{
    public Task<OperationResult<ThirdPartyChecksResponse>> ListAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new ListThirdPartyChecksQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<ThirdPartyCheckRow>> CheckAsync(ThirdPartyKindDto kind, Guid partyId, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new CheckThirdPartyCommand(dossier, (ThirdPartyKind)(int)kind, partyId), ct), cancellationToken);
}
