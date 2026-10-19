using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Policies;
using Lettra.Contracts.Policies;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using static Lettra.Api.Infrastructure.HttpResults;

namespace Lettra.Api.Endpoints;

/// <summary>Politiques du cabinet réglées par son administrateur (annexe F §3.2). Routes hors dossier.</summary>
internal static class PolicyEndpoints
{
    public static RouteGroupBuilder MapCabinetPolicies(this RouteGroupBuilder cabinet)
    {
        RouteGroupBuilder group = cabinet.MapGroup("/settings").WithTags("Cabinet settings");

        group.MapGet("/segregation-of-duties", GetAsync)
            .RequireCabinetPermission(Permission.CabinetAdministrer)
            .WithName("GetSegregationOfDuties")
            .Produces<SegregationOfDutiesSettings>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/segregation-of-duties", UpdateAsync)
            .RequireCabinetPermission(Permission.CabinetAdministrer)
            .WithName("UpdateSegregationOfDuties")
            .Produces<SegregationOfDutiesSettings>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return group;
    }

    private static async Task<IResult> GetAsync(Guid cabinetId, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<SegregationOfDutiesSettings> result = await queries
            .QueryAsync(new GetSegregationOfDutiesQuery(new OrganisationId(cabinetId)), cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    private static async Task<IResult> UpdateAsync(
        Guid cabinetId, SegregationOfDutiesSettings request, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Result<SegregationOfDutiesSettings> result = await commands
            .SendAsync(
                new UpdateSegregationOfDutiesCommand(new OrganisationId(cabinetId), request.Enabled, request.CreditNoteApprovalThresholdInclTax),
                cancellationToken)
            .ConfigureAwait(false);
        return Ok(result);
    }
}
