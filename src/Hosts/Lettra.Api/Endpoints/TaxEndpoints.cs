using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Tax;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Tax;
using static Lettra.Api.Infrastructure.HttpResults;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Déclarations fiscales du dossier (module Exports) : brouillon de CA3 ou de CA12 (partie ventes), DES du mois et son
/// fichier, calendrier fiscal et ses réglages. Des brouillons : rien n'est déposé auprès de l'administration.
/// </summary>
internal static class TaxEndpoints
{
    public static RouteGroupBuilder MapTax(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/tax").WithTags("Tax");

        group.MapGet("/vat-return", GetVatReturnAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("GetVatReturn")
            .Produces<VatReturnResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/des", GetDesAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("GetDes")
            .Produces<DesResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // Délais de paiement : tableau du rapport de gestion à la clôture et échéances au-delà du plafond légal.
        group.MapGet("/payment-delays", GetPaymentDelaysAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("GetPaymentDelays")
            .Produces<PaymentDelaysResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/des/file", ExportDesAsync)
            .RequirePermission(Permission.FecExporter)
            .WithName("ExportDes")
            .Produces(StatusCodes.Status200OK, contentType: DesContentType)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/calendar", GetCalendarAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("GetFiscalCalendar")
            .Produces<FiscalCalendarResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // Vue cabinet : ce qui est en retard ou dû dans les jours qui viennent.
        group.MapGet("/calendar/upcoming", GetUpcomingAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("GetUpcomingFiscalDeadlines")
            .Produces<IReadOnlyList<FiscalDeadlineResponse>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut("/calendar/filings", MarkFilingAsync)
            .RequirePermission(Permission.FecExporter)
            .WithName("MarkFiscalFiling")
            .Produces<FiscalDeadlineResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/settings", GetSettingsAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("GetFiscalSettings")
            .Produces<FiscalSettingsResponse>();

        group.MapPut("/settings", UpdateSettingsAsync)
            .RequirePermission(Permission.FecExporter)
            .WithName("UpdateFiscalSettings")
            .Produces<FiscalSettingsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Déclaration des honoraires (DAS 2) : bénéficiaires suivis et ce qu'ils ont reçu pendant l'exercice.
        group.MapGet("/fees", GetFeeReturnAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("GetFeeReturn")
            .Produces<FeeReturnResponse>();

        group.MapPut("/fees/{supplierId:guid}", SaveFeeBeneficiaryAsync)
            .RequirePermission(Permission.FecExporter)
            .WithName("SaveFeeBeneficiary")
            .Produces<FeeReturnResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireIfMatch();

        group.MapDelete("/fees/{supplierId:guid}", RemoveFeeBeneficiaryAsync)
            .RequirePermission(Permission.FecExporter)
            .WithName("RemoveFeeBeneficiary")
            .Produces<FeeReturnResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireIfMatch();

        return group;
    }

    private const string DesContentType = "text/csv; charset=utf-8";

    private static async Task<IResult> GetVatReturnAsync(DateOnly date, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new GetVatReturnQuery(tenant.Current.DossierId, date), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetDesAsync(int year, int month, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new GetDesQuery(tenant.Current.DossierId, year, month), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetPaymentDelaysAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken, DateOnly? closingDate = null) =>
        Ok(await queries.QueryAsync(new GetPaymentDelaysQuery(tenant.Current.DossierId, closingDate), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ExportDesAsync(int year, int month, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<DesFile> file = await queries.QueryAsync(new ExportDesQuery(tenant.Current.DossierId, year, month), cancellationToken).ConfigureAwait(false);
        return file.IsSuccess ? TypedResults.File(file.Value.Content, DesContentType, file.Value.FileName) : file.Errors.ToProblem();
    }

    private static async Task<IResult> GetCalendarAsync(DateOnly from, DateOnly to, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new GetFiscalCalendarQuery(tenant.Current.DossierId, from, to), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetUpcomingAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken, int days = FiscalCalendar.DueSoonDays) =>
        Ok(await queries.QueryAsync(new GetUpcomingFiscalDeadlinesQuery(tenant.Current.DossierId, days), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> MarkFilingAsync(MarkFiscalFilingRequest request, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken) =>
        Ok(await commands.SendAsync(new MarkFiscalFilingCommand(tenant.Current.DossierId, request), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetFeeReturnAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken, DateOnly? date = null) =>
        Ok(await queries.QueryAsync(new GetFeeReturnQuery(tenant.Current.DossierId, date), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> SaveFeeBeneficiaryAsync(
        Guid supplierId, SaveFeeBeneficiaryRequest request, HttpContext http, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        DossierId dossier = tenant.Current.DossierId;
        Result saved = await commands.SendAsync(new SaveFeeBeneficiaryCommand(dossier, new SupplierId(supplierId), request, http.IfMatchVersion()), cancellationToken)
            .ConfigureAwait(false);
        return await FeeReturnAfterAsync(saved, supplierId, request.FiscalYearStart, dossier, queries, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> RemoveFeeBeneficiaryAsync(
        Guid supplierId, DateOnly date, HttpContext http, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        Result removed = await commands.SendAsync(new RemoveFeeBeneficiaryCommand(dossier, new SupplierId(supplierId), http.IfMatchVersion()), cancellationToken)
            .ConfigureAwait(false);
        return await FeeReturnAfterAsync(removed, supplierId, date, dossier, queries, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>La déclaration relue après une écriture ; un refus de version renvoie la ligne actuelle (ADR-0013).</summary>
    private static async Task<IResult> FeeReturnAfterAsync(
        Result written, Guid supplierId, DateOnly date, DossierId dossier, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Task<Result<FeeReturnResponse>> Read() => queries.QueryAsync(new GetFeeReturnQuery(dossier, date), cancellationToken);
        long VersionOf(FeeReturnResponse view) => view.Rows.FirstOrDefault(r => r.SupplierId == supplierId)?.Version ?? 0;
        return written.IsFailure
            ? await ConcurrencyResults.WriteFailureAsync(written.Errors, Read, VersionOf).ConfigureAwait(false)
            : Versioned(await Read().ConfigureAwait(false), VersionOf);
    }

    private static async Task<IResult> GetSettingsAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new GetFiscalSettingsQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> UpdateSettingsAsync(UpdateFiscalSettingsRequest request, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken) =>
        Ok(await commands.SendAsync(new UpdateFiscalSettingsCommand(tenant.Current.DossierId, request), cancellationToken).ConfigureAwait(false));
}
