using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Dossiers;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Api.Endpoints;

/// <summary>Administration des dossiers d'un cabinet. Routes hors dossier : la permission porte sur le cabinet.</summary>
internal static class DossierEndpoints
{
    public static RouteGroupBuilder MapDossiers(this RouteGroupBuilder cabinet)
    {
        RouteGroupBuilder group = cabinet.MapGroup("/dossiers").WithTags("Dossiers");

        group.MapPost("/", CreateAsync)
            .RequireCabinetPermission(Permission.DossiersCreer)
            .WithName("CreateDossier")
            .Produces<DossierSummary>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return group;
    }

    private static async Task<IResult> CreateAsync(
        Guid cabinetId,
        CreateDossierRequest request,
        ICommandDispatcher commands,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Result<DossierSummary> created = await commands
            .SendAsync(new CreateDossierCommand(new OrganisationId(cabinetId), request.Name), cancellationToken).ConfigureAwait(false);
        return created.IsSuccess
            ? TypedResults.Created($"/api/v1/cabinets/{cabinetId}/dossiers/{created.Value.Id}", created.Value)
            : created.Errors.ToProblem();
    }
}
