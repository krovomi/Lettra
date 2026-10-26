using System.Text.Json;
using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Assistant;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Assistant;

/// <summary>
/// Un message de l'assistant IA du poste (MCP). Le pipeline vérifie le dossier et <see cref="Permission.IaUtiliser"/> ;
/// le cabinet doit avoir ouvert le point <see cref="AssistantFeature.ExternalAssistant"/>, fermé par défaut.
/// </summary>
public sealed record McpMessageQuery(DossierId Dossier, string? SessionId, JsonElement Message) : IQuery<Result<McpReply>>, IRequirePermission
{
    public Permission Permission => Permission.IaUtiliser;
}

/// <summary>Fin d'une session de l'assistant IA du poste.</summary>
public sealed record EndMcpSessionQuery(DossierId Dossier, string SessionId) : IQuery<Result<bool>>, IRequirePermission
{
    public Permission Permission => Permission.IaUtiliser;
}

internal sealed class McpMessageHandler(
    ITenantContext tenant,
    ICurrentUser user,
    IPermissionService permissions,
    IAiUsagePolicy policy,
    INamedEntityDetection namedEntities,
    IMcpServer server) : IQueryHandler<McpMessageQuery, Result<McpReply>>, IQueryHandler<EndMcpSessionQuery, Result<bool>>
{
    public async Task<Result<McpReply>> HandleAsync(McpMessageQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Result<McpCaller> caller = await CallerAsync(query.Dossier, cancellationToken).ConfigureAwait(false);
        return caller.IsSuccess
            ? await server.HandleAsync(query.SessionId, query.Message, caller.Value, cancellationToken).ConfigureAwait(false)
            : Result<McpReply>.Failure(caller.Errors);
    }

    public async Task<Result<bool>> HandleAsync(EndMcpSessionQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Result<McpCaller> caller = await CallerAsync(query.Dossier, cancellationToken).ConfigureAwait(false);
        return caller.IsSuccess ? server.End(query.SessionId, caller.Value) : Result<bool>.Failure(caller.Errors);
    }

    private async Task<Result<McpCaller>> CallerAsync(DossierId dossier, CancellationToken cancellationToken)
    {
        OrganisationId organisation = tenant.Current.OrganisationId;
        AiSettings settings = await policy.GetSettingsAsync(organisation, cancellationToken).ConfigureAwait(false);
        if (AiErrors.CheckPolicy(settings, AssistantFeature.ExternalAssistant, dossier, namedEntities) is { } refused)
        {
            return refused;
        }

        IReadOnlySet<Permission> granted = await permissions.GetEffectiveAsync(user.UserId, dossier, cancellationToken).ConfigureAwait(false);
        return new McpCaller(organisation, dossier, user.UserId, granted, settings);
    }
}
