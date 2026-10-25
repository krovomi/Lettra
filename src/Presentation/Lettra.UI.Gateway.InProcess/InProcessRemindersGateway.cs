using Lettra.Contracts.Common;
using Lettra.Contracts.Reminders;
using Lettra.Exports.Application.Reminders;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Mêmes cas d'usage et mêmes permissions que les routes <c>/reminders</c> de l'API.</summary>
internal sealed class InProcessRemindersGateway(IServiceScopeFactory scopes) : IRemindersGateway
{
    public Task<OperationResult<ReminderOverviewResponse>> GetOverviewAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetReminderOverviewQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<ReminderPreviewResponse>> GetPreviewAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetReminderPreviewQuery(dossier, invoiceId), ct), cancellationToken);

    public Task<OperationResult<PaymentReminderResponse>> SendAsync(Guid invoiceId, ReminderChannelDto channel, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new SendReminderCommand(dossier, invoiceId, new SendReminderRequest(channel)), ct), cancellationToken);

    public Task<OperationResult<RunRemindersResponse>> RunDueAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new RunDueRemindersCommand(dossier), ct), cancellationToken);

    public Task<OperationResult<PagedResponse<PaymentReminderResponse>>> ListHistoryAsync(ReminderHistoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new ListPaymentRemindersQuery(
            dossier, query.InvoiceId, null, query.Level, query.Outcome, query.Channel, query.From, query.To, query.Search, query.Sort, query.Descending, query.Page, query.PageSize), ct), cancellationToken);
    }

    public Task<OperationResult<ReminderSettingsResponse>> UpdateSettingsAsync(UpdateReminderSettingsRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new UpdateReminderSettingsCommand(dossier, request), ct), cancellationToken);
}
