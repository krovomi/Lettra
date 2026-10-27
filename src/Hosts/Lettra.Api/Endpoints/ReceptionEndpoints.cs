using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Exports;
using Lettra.Domain.Common;
using Lettra.Exports.Application.EInvoicing;
using Lettra.Exports.Application.EInvoicing.Reception;
using static Lettra.Api.Infrastructure.HttpResults;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Réception des factures fournisseurs par la plateforme agréée (réforme de la facturation électronique) et annuaire
/// central : relever les factures mises à disposition, les lire, y répondre, et chercher une entreprise à l'annuaire.
/// </summary>
internal static class ReceptionEndpoints
{
    public static RouteGroupBuilder MapReception(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/e-invoicing").WithTags("Exports");

        group.MapGet("/received", ListAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("ListReceivedInvoices")
            .Produces<ReceivedInvoicesResponse>();

        // Synchrone : la plateforme rend les factures mises à disposition ; seules les nouvelles s'enregistrent.
        group.MapPost("/received/fetch", FetchAsync)
            .RequirePermission(Permission.FacturesRecuesTraiter)
            .WithName("FetchReceivedInvoices")
            .Produces<FetchReceivedInvoicesResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/received/{id:guid}", GetAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("GetReceivedInvoice")
            .Produces<ReceivedInvoiceResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/received/{id:guid}/file", DownloadAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("DownloadReceivedInvoice")
            .Produces(StatusCodes.Status200OK, contentType: "application/xml")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/received/{id:guid}/responses", RespondAsync)
            .RequirePermission(Permission.FacturesRecuesTraiter)
            .WithName("RespondToReceivedInvoice")
            .Produces<ReceivedInvoiceResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Un SIREN absent de l'annuaire n'est pas une erreur : la réponse le dit (Entry vide).
        group.MapGet("/directory/{siren}", LookupAsync)
            .RequirePermission(Permission.TiersConsulter)
            .WithName("LookupEInvoicingDirectory")
            .Produces<DirectoryLookupResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return group;
    }

    private static async Task<IResult> ListAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new GetReceivedInvoicesQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> FetchAsync(ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken) =>
        Ok(await commands.SendAsync(new FetchReceivedInvoicesCommand(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetAsync(Guid id, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new GetReceivedInvoiceQuery(tenant.Current.DossierId, id), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> DownloadAsync(Guid id, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<EInvoiceFile> file = await queries.QueryAsync(new DownloadReceivedInvoiceQuery(tenant.Current.DossierId, id), cancellationToken).ConfigureAwait(false);
        return file.IsSuccess
            ? TypedResults.File(file.Value.Content, file.Value.ContentType, file.Value.FileName)
            : file.Errors.ToProblem();
    }

    private static async Task<IResult> RespondAsync(
        Guid id, RespondToReceivedInvoiceRequest request, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken) =>
        Ok(await commands.SendAsync(new RespondToReceivedInvoiceCommand(tenant.Current.DossierId, id, request), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> LookupAsync(string siren, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new LookupDirectoryQuery(tenant.Current.DossierId, siren), cancellationToken).ConfigureAwait(false));
}
