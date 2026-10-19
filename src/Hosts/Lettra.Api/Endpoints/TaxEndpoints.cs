using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Tax;
using Lettra.Domain.Common;
using Lettra.Exports.Application.Tax;

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

        return group;
    }

    private const string DesContentType = "text/csv; charset=utf-8";

    private static async Task<IResult> GetVatReturnAsync(DateOnly date, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        (await queries.QueryAsync(new GetVatReturnQuery(tenant.Current.DossierId, date), cancellationToken).ConfigureAwait(false)).ToHttp();

    private static async Task<IResult> GetDesAsync(int year, int month, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        (await queries.QueryAsync(new GetDesQuery(tenant.Current.DossierId, year, month), cancellationToken).ConfigureAwait(false)).ToHttp();

    private static async Task<IResult> ExportDesAsync(int year, int month, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<DesFile> file = await queries.QueryAsync(new ExportDesQuery(tenant.Current.DossierId, year, month), cancellationToken).ConfigureAwait(false);
        return file.IsSuccess ? TypedResults.File(file.Value.Content, DesContentType, file.Value.FileName) : file.Errors.ToProblem();
    }

    private static async Task<IResult> GetCalendarAsync(DateOnly from, DateOnly to, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        (await queries.QueryAsync(new GetFiscalCalendarQuery(tenant.Current.DossierId, from, to), cancellationToken).ConfigureAwait(false)).ToHttp();

    private static async Task<IResult> GetUpcomingAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken, int days = FiscalCalendar.DueSoonDays) =>
        (await queries.QueryAsync(new GetUpcomingFiscalDeadlinesQuery(tenant.Current.DossierId, days), cancellationToken).ConfigureAwait(false)).ToHttp();

    private static async Task<IResult> MarkFilingAsync(MarkFiscalFilingRequest request, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken) =>
        (await commands.SendAsync(new MarkFiscalFilingCommand(tenant.Current.DossierId, request), cancellationToken).ConfigureAwait(false)).ToHttp();

    private static async Task<IResult> GetSettingsAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        (await queries.QueryAsync(new GetFiscalSettingsQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false)).ToHttp();

    private static async Task<IResult> UpdateSettingsAsync(UpdateFiscalSettingsRequest request, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken) =>
        (await commands.SendAsync(new UpdateFiscalSettingsCommand(tenant.Current.DossierId, request), cancellationToken).ConfigureAwait(false)).ToHttp();

    private static IResult ToHttp<T>(this Result<T> result) => result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
}
