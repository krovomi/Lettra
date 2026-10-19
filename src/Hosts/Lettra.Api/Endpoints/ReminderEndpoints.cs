using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Common;
using Lettra.Contracts.Reminders;
using Lettra.Domain.Common;
using Lettra.Exports.Application.Reminders;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Relances des factures échues (module Exports, vague 7) : vue d'ensemble, aperçu du texte, envoi par e-mail ou relance
/// notée à la main, envoi des relances du jour, historique filtré et trié, scénario du dossier.
/// </summary>
internal static class ReminderEndpoints
{
    public static RouteGroupBuilder MapReminders(this RouteGroupBuilder api)
    {
        RouteGroupBuilder reminders = api.MapGroup("/reminders").WithTags("Reminders");

        reminders.MapGet("/", (ITenantContext tenant, IQueryDispatcher queries, CancellationToken ct) =>
                QueryAsync(queries, new GetReminderOverviewQuery(tenant.Current.DossierId), ct))
            .RequirePermission(Permission.DossierLire)
            .WithName("GetReminderOverview")
            .Produces<ReminderOverviewResponse>();

        reminders.MapGet("/history", ListHistoryAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("ListPaymentReminders")
            .Produces<PagedResponse<PaymentReminderResponse>>();

        reminders.MapGet("/invoices/{invoiceId:guid}/preview", (Guid invoiceId, ITenantContext tenant, IQueryDispatcher queries, CancellationToken ct) =>
                QueryAsync(queries, new GetReminderPreviewQuery(tenant.Current.DossierId, invoiceId), ct))
            .RequirePermission(Permission.DossierLire)
            .WithName("GetReminderPreview")
            .Produces<ReminderPreviewResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        reminders.MapPost("/invoices/{invoiceId:guid}", (Guid invoiceId, SendReminderRequest request, ITenantContext tenant, ICommandDispatcher commands, CancellationToken ct) =>
                SendAsync(commands, new SendReminderCommand(tenant.Current.DossierId, invoiceId, request), ct))
            .RequirePermission(Permission.FactureEmettre)
            .WithName("SendReminder")
            .Produces<PaymentReminderResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        reminders.MapPost("/run", (ITenantContext tenant, ICommandDispatcher commands, CancellationToken ct) =>
                SendAsync(commands, new RunDueRemindersCommand(tenant.Current.DossierId), ct))
            .RequirePermission(Permission.FactureEmettre)
            .WithName("RunDueReminders")
            .Produces<RunRemindersResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        reminders.MapGet("/settings", (ITenantContext tenant, IQueryDispatcher queries, CancellationToken ct) =>
                QueryAsync(queries, new GetReminderSettingsQuery(tenant.Current.DossierId), ct))
            .RequirePermission(Permission.DossierLire)
            .WithName("GetReminderSettings")
            .Produces<ReminderSettingsResponse>();

        reminders.MapPut("/settings", (UpdateReminderSettingsRequest request, ITenantContext tenant, ICommandDispatcher commands, CancellationToken ct) =>
                SendAsync(commands, new UpdateReminderSettingsCommand(tenant.Current.DossierId, request), ct))
            .RequirePermission(Permission.CatalogueGerer)
            .WithName("UpdateReminderSettings")
            .Produces<ReminderSettingsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return reminders;
    }

    private static Task<IResult> ListHistoryAsync(
        ITenantContext tenant,
        IQueryDispatcher queries,
        CancellationToken cancellationToken,
        Guid? invoiceId = null,
        Guid? customerId = null,
        ReminderLevelDto? level = null,
        ReminderOutcomeDto? outcome = null,
        ReminderChannelDto? channel = null,
        DateOnly? from = null,
        DateOnly? to = null,
        string? search = null,
        ReminderSortDto sort = ReminderSortDto.Date,
        bool descending = true,
        int page = 1,
        int pageSize = 50) =>
        QueryAsync(queries, new ListPaymentRemindersQuery(
            tenant.Current.DossierId, invoiceId, customerId, level, outcome, channel, from, to, search, sort, descending, page, pageSize), cancellationToken);

    private static async Task<IResult> QueryAsync<T>(IQueryDispatcher queries, IQuery<Result<T>> query, CancellationToken cancellationToken)
    {
        Result<T> result = await queries.QueryAsync(query, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }

    private static async Task<IResult> SendAsync<T>(ICommandDispatcher commands, ICommand<Result<T>> command, CancellationToken cancellationToken)
    {
        Result<T> result = await commands.SendAsync(command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }
}
