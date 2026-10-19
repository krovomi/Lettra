using Lettra.Contracts.Common;
using Lettra.Exports.Application.AuditKit;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Même requête et même permission que la route <c>/exports/audit-kit</c> de l'API.</summary>
internal sealed class InProcessAuditKitGateway(IServiceScopeFactory scopes) : IAuditKitGateway
{
    public Task<OperationResult<AuditKitDocument>> PrepareAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, async (sp, dossier, ct) =>
        {
            OperationResult<AuditKitFile> kit = await InProcessScope.QueryAsync(sp, new GetAuditKitQuery(dossier, from, to), ct).ConfigureAwait(false);
            return kit is { IsSuccess: true, Value: { } value }
                ? OperationResult<AuditKitDocument>.Success(new AuditKitDocument(value.FileName, value.Content))
                : OperationResult<AuditKitDocument>.Failure(kit.Errors);
        }, cancellationToken);
}
