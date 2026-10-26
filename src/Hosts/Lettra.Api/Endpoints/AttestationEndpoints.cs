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
        return group;
    }

    private static async Task<IResult> ListAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new ListAttestationsQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false));

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
