using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Decisions;
using Lettra.Contracts.Decisions;
using Lettra.Domain.Common;
using Lettra.Domain.Decisions;
using Lettra.Domain.SharedKernel;
using static Lettra.Api.Infrastructure.HttpResults;

namespace Lettra.Api.Endpoints;

/// <summary>Décisions à prendre du dossier ouvert (en-tête <c>X-Dossier</c>) et son cadran d'autonomie (docs/decisions-a-prendre.md).</summary>
internal static class DecisionEndpoints
{
    public static RouteGroupBuilder MapDecisions(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/decisions").WithTags("Decisions");

        group.MapGet("/", ListAsync)
            .RequirePermission(Permission.DecisionsTraiter)
            .WithName("ListDecisions")
            .Produces<DecisionsResponse>();

        group.MapPost("/refresh", RefreshAsync)
            .RequirePermission(Permission.DecisionsTraiter)
            .WithName("RefreshDecisions")
            .Produces<DecisionsResponse>();

        group.MapPost("/{decisionId:guid}/answer", AnswerAsync)
            .RequirePermission(Permission.DecisionsTraiter)
            .WithName("AnswerDecision")
            .Produces<DecisionsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireIfMatch();

        group.MapPut("/policy", SetPolicyAsync)
            .RequirePermission(Permission.DecisionsRegler)
            .WithName("SetDecisionPolicy")
            .Produces<DecisionsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireIfMatch();
        return group;
    }

    private static async Task<IResult> ListAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new ListDecisionsQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> RefreshAsync(ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        Result refreshed = await commands.SendAsync(new RefreshDecisionsCommand(dossier), cancellationToken).ConfigureAwait(false);
        return refreshed.IsFailure
            ? refreshed.Errors.ToProblem()
            : Ok(await queries.QueryAsync(new ListDecisionsQuery(dossier), cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> AnswerAsync(
        Guid decisionId, AnswerDecisionRequest request, HttpContext http, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        DossierId dossier = tenant.Current.DossierId;
        Result answered = await commands.SendAsync(
            new AnswerDecisionCommand(dossier, new DecisionId(decisionId), request.Choice, request.SnoozeUntil, http.IfMatchVersion()), cancellationToken)
            .ConfigureAwait(false);
        Task<Result<DecisionsResponse>> Read() => queries.QueryAsync(new ListDecisionsQuery(dossier), cancellationToken);
        long VersionOf(DecisionsResponse list) => list.Rows.FirstOrDefault(r => r.Id == decisionId)?.Version ?? 0;
        return answered.IsFailure
            ? await ConcurrencyResults.WriteFailureAsync(answered.Errors, Read, VersionOf).ConfigureAwait(false)
            : Versioned(await Read().ConfigureAwait(false), VersionOf);
    }

    private static async Task<IResult> SetPolicyAsync(
        SetDecisionPolicyRequest request, HttpContext http, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        DossierId dossier = tenant.Current.DossierId;
        Result set = await commands.SendAsync(new SetDecisionPolicyCommand(dossier, request.Level, http.IfMatchVersion()), cancellationToken).ConfigureAwait(false);
        Task<Result<DecisionsResponse>> Read() => queries.QueryAsync(new ListDecisionsQuery(dossier), cancellationToken);
        static long VersionOf(DecisionsResponse list) => list.PolicyVersion ?? 0;
        return set.IsFailure
            ? await ConcurrencyResults.WriteFailureAsync(set.Errors, Read, VersionOf).ConfigureAwait(false)
            : Versioned(await Read().ConfigureAwait(false), VersionOf);
    }
}
