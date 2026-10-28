using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Parties;
using Lettra.Contracts.Vigilance;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Domain.Vigilance;
using static Lettra.Api.Infrastructure.HttpResults;

namespace Lettra.Api.Endpoints;

/// <summary>Attestations de vigilance des fournisseurs du dossier ouvert (en-tête <c>X-Dossier</c>).</summary>
internal static class AttestationEndpoints
{
    public static RouteGroupBuilder MapAttestations(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/attestations").WithTags("Suppliers");

        group.MapGet("/", ListAsync)
            .RequirePermission(Permission.TiersConsulter)
            .WithName("ListAttestations")
            .Produces<SupplierAttestationsResponse>();

        group.MapPut("/{supplierId:guid}", SaveAsync)
            .RequirePermission(Permission.TiersModifier)
            .WithName("SaveAttestation")
            .Produces<SupplierAttestationsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireIfMatch();

        group.MapPost("/{supplierId:guid}/verification", VerifyAsync)
            .RequirePermission(Permission.TiersModifier)
            .WithName("VerifyAttestationCode")
            .Produces<AttestationVerificationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return group;
    }

    private static async Task<IResult> ListAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new ListAttestationsQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false));

    /// <summary>Un code non reconnu est une réponse de l'URSSAF, pas une erreur : 200 avec la réponse.</summary>
    private static async Task<IResult> VerifyAsync(
        Guid supplierId, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        Result<UrssafAnswer> answer = await commands.SendAsync(new VerifyAttestationCodeCommand(dossier, new SupplierId(supplierId)), cancellationToken)
            .ConfigureAwait(false);
        if (answer.IsFailure)
        {
            return Ok(Result<AttestationVerificationResponse>.Failure(answer.Errors));
        }

        Result<SupplierAttestationsResponse> list = await queries.QueryAsync(new ListAttestationsQuery(dossier), cancellationToken).ConfigureAwait(false);
        return Ok(list.IsFailure
            ? Result<AttestationVerificationResponse>.Failure(list.Errors)
            : new AttestationVerificationResponse((UrssafAnswerDto)(int)answer.Value, list.Value));
    }

    private static async Task<IResult> SaveAsync(
        Guid supplierId, SaveAttestationRequest request, HttpContext http, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        DossierId dossier = tenant.Current.DossierId;
        SupplierId supplier = new(supplierId);
        Result saved = await commands.SendAsync(new SaveAttestationCommand(dossier, supplier, request.ToDraft(), http.IfMatchVersion()), cancellationToken)
            .ConfigureAwait(false);
        Task<Result<SupplierAttestationsResponse>> Read() => queries.QueryAsync(new ListAttestationsQuery(dossier), cancellationToken);
        long VersionOf(SupplierAttestationsResponse list) => list.Rows.FirstOrDefault(r => r.SupplierId == supplierId)?.Version ?? 0;
        return saved.IsFailure
            ? await ConcurrencyResults.WriteFailureAsync(saved.Errors, Read, VersionOf).ConfigureAwait(false)
            : Versioned(await Read().ConfigureAwait(false), VersionOf);
    }
}
