using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Exports;
using Lettra.Domain.Common;
using Lettra.Domain.Billing;
using Lettra.Exports.Application.EInvoicing;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Exports.Application.Fec;
using Microsoft.AspNetCore.Mvc;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Exports du dossier (module Exports, phase 5) : FEC des écritures de vente et validateur intégré, facture électronique
/// EN 16931 (CII ou UBL) d'une pièce validée et sa transmission à la plateforme agréée (statuts, encaissement), point des
/// créances du tableau de bord.
/// </summary>
internal static class ExportEndpoints
{
    public static RouteGroupBuilder MapExports(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/exports").WithTags("Exports");

        // La consultation du journal des ventes lit les mêmes écritures que le FEC : mêmes module et branchement.
        api.MapAccounting();
        api.MapBanking();
        api.MapReminders();

        // Les déclarations fiscales lisent les mêmes pièces et le même journal.
        api.MapTax();

        group.MapGet("/fec", ExportFecAsync)
            .RequirePermission(Permission.FecExporter)
            .WithName("ExportFec")
            .Produces(StatusCodes.Status200OK, contentType: FecContentType)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Corps brut : le fichier tel que le logiciel comptable l'a produit (UTF-8 ou ISO 8859-15).
        group.MapPost("/fec/validation", ValidateFecAsync)
            .RequirePermission(Permission.FecExporter)
            .WithName("ValidateFec")
            .WithMetadata(new RequestSizeLimitAttribute(ValidateFecQuery.MaxContentLength))
            .Produces<FecValidationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // Le XML CII est aussi celui que le client joint au PDF pour en faire un Factur-X.
        api.MapGet("/invoices/{id:guid}/e-invoice", GetEInvoiceAsync)
            .WithTags("Exports")
            .RequirePermission(Permission.DossierLire)
            .WithName("GetEInvoice")
            .Produces(StatusCodes.Status200OK, contentType: "application/xml")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Tableau de bord : factures en retard et état des transmissions de toutes les pièces validées.
        api.MapGet("/receivables/summary", GetReceivablesSummaryAsync)
            .WithTags("Exports")
            .RequirePermission(Permission.DossierLire)
            .WithName("GetReceivablesSummary")
            .Produces<ReceivablesSummaryResponse>();

        RouteGroupBuilder platform = api.MapGroup("/invoices/{id:guid}/platform-transmission").WithTags("Exports");

        platform.MapGet("/", GetPlatformTransmissionAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("GetPlatformTransmission")
            .Produces<PlatformTransmissionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Synchrone : la plateforme accuse le dépôt (Déposée ou Rejetée) dans la réponse ; la pièce déposée est émise.
        platform.MapPost("/", TransmitInvoiceAsync)
            .RequirePermission(Permission.FactureEmettre)
            .WithName("TransmitInvoice")
            .Produces<PlatformTransmissionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        platform.MapPost("/refresh", RefreshPlatformStatusAsync)
            .RequirePermission(Permission.FactureEmettre)
            .WithName("RefreshPlatformStatus")
            .Produces<PlatformTransmissionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        platform.MapPost("/payments", ReportPaymentAsync)
            .RequirePermission(Permission.FactureEmettre)
            .WithName("ReportPayment")
            .Produces<PlatformTransmissionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return group;
    }

    private const string FecContentType = "text/plain; charset=iso-8859-15";

    private static async Task<IResult> ExportFecAsync(DateOnly from, DateOnly to, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<FecFile> file = await queries.QueryAsync(new ExportFecQuery(tenant.Current.DossierId, from, to), cancellationToken).ConfigureAwait(false);
        return file.IsSuccess
            ? TypedResults.File(file.Value.Content, FecContentType, file.Value.FileName)
            : file.Errors.ToProblem();
    }

    private static async Task<IResult> ValidateFecAsync(
        string fileName, HttpRequest request, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken, DateOnly? from = null, DateOnly? to = null)
    {
        using MemoryStream content = new();
        await request.Body.CopyToAsync(content, cancellationToken).ConfigureAwait(false);
        Result<FecValidationResponse> report = await queries
            .QueryAsync(new ValidateFecQuery(tenant.Current.DossierId, fileName, content.GetBuffer().AsMemory(0, (int)content.Length), from, to), cancellationToken)
            .ConfigureAwait(false);
        return report.IsSuccess ? TypedResults.Ok(report.Value) : report.Errors.ToProblem();
    }

    private static async Task<IResult> GetEInvoiceAsync(
        Guid id, EInvoiceFormatDto format, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<EInvoiceFile> file = await queries
            .QueryAsync(new GetEInvoiceQuery(tenant.Current.DossierId, new InvoiceId(id), format), cancellationToken)
            .ConfigureAwait(false);
        return file.IsSuccess
            ? TypedResults.File(file.Value.Content, file.Value.ContentType, file.Value.FileName)
            : file.Errors.ToProblem();
    }

    private static async Task<IResult> GetPlatformTransmissionAsync(Guid id, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new GetPlatformTransmissionQuery(tenant.Current.DossierId, new InvoiceId(id)), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> TransmitInvoiceAsync(
        Guid id, TransmitInvoiceRequest? request, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken) =>
        Ok(await commands.SendAsync(
            new TransmitInvoiceCommand(tenant.Current.DossierId, new InvoiceId(id), request?.Format ?? EInvoiceFormatDto.Cii), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> RefreshPlatformStatusAsync(Guid id, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken) =>
        Ok(await commands.SendAsync(new RefreshPlatformStatusCommand(tenant.Current.DossierId, new InvoiceId(id)), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ReportPaymentAsync(
        Guid id, ReportPaymentRequest request, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken) =>
        Ok(await commands.SendAsync(
            new ReportPaymentCommand(tenant.Current.DossierId, new InvoiceId(id), request.Date, request.Amount), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetReceivablesSummaryAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<ReceivablesSummaryResponse> summary = await queries
            .QueryAsync(new GetReceivablesSummaryQuery(tenant.Current.DossierId), cancellationToken)
            .ConfigureAwait(false);
        return summary.IsSuccess ? TypedResults.Ok(summary.Value) : summary.Errors.ToProblem();
    }

    private static IResult Ok(Result<PlatformTransmissionResponse> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
}
