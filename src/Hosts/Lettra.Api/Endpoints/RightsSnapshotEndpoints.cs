using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Identity;
using Lettra.Contracts.Identity;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Identity.Oidc.Offline;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Instantané de droits signé pour le hors ligne (annexe F §4). Hors dossier : le poste demande ses droits sur tous
/// les dossiers qu'il réplique, et le cas d'usage ne signe, dossier par dossier, que ce que le registre accorde.
/// </summary>
internal static class RightsSnapshotEndpoints
{
    /// <summary>Clés publiques de vérification, lues par les postes avant de passer hors ligne. Rien de secret.</summary>
    public const string KeysPath = "/.well-known/lettra-rights-keys";

    public static IEndpointRouteBuilder MapRightsSnapshot(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/v1/me")
            .RequireAuthorization(LettraPolicies.User)
            .AddEndpointFilter<CsrfEndpointFilter>()
            .WithTags("Identity")
            .MapPost("/rights-snapshot", IssueAsync)
            // Vérifiée par dossier dans le cas d'usage : un dossier sans droit de données n'entre pas dans l'instantané.
            .WithMetadata(new RequiredPermissionMetadata(Permission.DossierLire))
            .WithName("IssueRightsSnapshot")
            .Produces<RightsSnapshotResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        app.MapGet(KeysPath, (EcdsaRightsSnapshotIssuer issuer) => TypedResults.Ok(issuer.PublicKeys()))
            .AllowAnonymous()
            .WithTags("Identity")
            .WithName("RightsSnapshotKeys")
            .Produces<RightsSnapshotKeysResponse>();

        return app;
    }

    private static async Task<IResult> IssueAsync(IssueRightsSnapshotRequest request, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        IReadOnlyList<DossierId> dossiers = (request.DossierIds ?? []).Select(id => new DossierId(id)).ToList();
        Result<RightsSnapshotResponse> issued = await commands
            .SendAsync(new IssueRightsSnapshotCommand(request.DeviceId, dossiers), cancellationToken)
            .ConfigureAwait(false);
        return issued.IsSuccess ? TypedResults.Ok(issued.Value) : issued.Errors.ToProblem();
    }
}
