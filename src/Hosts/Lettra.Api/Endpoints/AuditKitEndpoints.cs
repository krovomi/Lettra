using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Common;
using Lettra.Exports.Application.AuditKit;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Kit contrôle fiscal du dossier (vague 7) : une archive ZIP horodatée pour une période (FEC, attestation d'intégrité,
/// piste d'audit fiable, XML des pièces, transmissions à la plateforme). Rien n'est envoyé à l'administration.
/// </summary>
internal static class AuditKitEndpoints
{
    private const string ZipContentType = "application/zip";

    public static RouteGroupBuilder MapAuditKit(this RouteGroupBuilder api)
    {
        api.MapGet("/exports/audit-kit", GetAuditKitAsync)
            .WithTags("Exports")
            .RequirePermission(Permission.ArchivesExporter)
            .WithName("GetAuditKit")
            .Produces(StatusCodes.Status200OK, contentType: ZipContentType)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        return api;
    }

    private static async Task<IResult> GetAuditKitAsync(DateOnly from, DateOnly to, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<AuditKitFile> kit = await queries.QueryAsync(new GetAuditKitQuery(tenant.Current.DossierId, from, to), cancellationToken).ConfigureAwait(false);
        return kit.IsSuccess
            ? TypedResults.File(kit.Value.Content, ZipContentType, kit.Value.FileName)
            : kit.Errors.ToProblem();
    }
}
