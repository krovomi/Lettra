using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Integrity;
using Lettra.Contracts.Integrity;
using Lettra.Domain.Common;

namespace Lettra.Api.Endpoints;

/// <summary>Chaîne anti-fraude du dossier : contrôle à la demande, clôtures signées, archives scellées et registre des clés.</summary>
internal static class IntegrityEndpoints
{
    public static RouteGroupBuilder MapIntegrity(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/integrity").WithTags("Integrity");

        group.MapPost("/verification", VerifyAsync)
            .RequirePermission(Permission.IntegriteVerifier)
            .WithName("VerifyDossierIntegrity")
            .Produces<IntegrityReportResponse>();

        group.MapGet("/closings", ListClosingsAsync)
            .RequirePermission(Permission.IntegriteVerifier)
            .WithName("ListClosings")
            .Produces<IReadOnlyList<ClosingSummary>>();

        group.MapPost("/closings", CloseAsync)
            .RequirePermission(Permission.ClotureEffectuer)
            .WithName("CloseDuePeriods")
            .Produces<IReadOnlyList<ClosingSummary>>();

        group.MapGet("/closings/{number:int}/archive", ArchiveAsync)
            .RequirePermission(Permission.ArchivesExporter)
            .WithName("GetSealedArchive")
            .Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/keys", ListKeysAsync)
            .RequirePermission(Permission.IntegriteVerifier)
            .WithName("ListIntegrityKeys")
            .Produces<IReadOnlyList<IntegrityKeySummary>>();

        group.MapPost("/keys/rotation", RotateKeysAsync)
            .RequirePermission(Permission.IntegriteClesGerer)
            .WithName("RotateIntegrityKeys")
            .Produces<IReadOnlyList<IntegrityKeySummary>>();

        return group;
    }

    private static async Task<IResult> ListKeysAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<IntegrityKeySummary>> keys = await queries
            .QueryAsync(new ListIntegrityKeysQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false);
        return keys.IsSuccess ? TypedResults.Ok(keys.Value) : keys.Errors.ToProblem();
    }

    private static async Task<IResult> RotateKeysAsync(ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<IntegrityKeySummary>> keys = await commands
            .SendAsync(new RotateIntegrityKeysCommand(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false);
        return keys.IsSuccess ? TypedResults.Ok(keys.Value) : keys.Errors.ToProblem();
    }

    private static async Task<IResult> VerifyAsync(ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        Result<IntegrityReportResponse> report = await commands
            .SendAsync(new VerifyDossierIntegrityCommand(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false);
        return report.IsSuccess ? TypedResults.Ok(report.Value) : report.Errors.ToProblem();
    }

    private static async Task<IResult> ListClosingsAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<ClosingSummary>> closings = await queries
            .QueryAsync(new ListClosingsQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false);
        return closings.IsSuccess ? TypedResults.Ok(closings.Value) : closings.Errors.ToProblem();
    }

    private static async Task<IResult> CloseAsync(ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<ClosingSummary>> closed = await commands
            .SendAsync(new CloseDuePeriodsCommand(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false);
        return closed.IsSuccess ? TypedResults.Ok(closed.Value) : closed.Errors.ToProblem();
    }

    private static async Task<IResult> ArchiveAsync(int number, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<SealedArchive> archive = await queries
            .QueryAsync(new GetSealedArchiveQuery(tenant.Current.DossierId, number), cancellationToken).ConfigureAwait(false);
        return archive.IsSuccess
            ? TypedResults.File(archive.Value.Content, "application/zip", archive.Value.FileName)
            : archive.Errors.ToProblem();
    }
}
