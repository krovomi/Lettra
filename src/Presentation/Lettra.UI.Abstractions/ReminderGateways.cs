using Lettra.Contracts.Common;
using Lettra.Contracts.Reminders;

namespace Lettra.UI.Abstractions;

/// <summary>Filtre et tri de l'historique des relances.</summary>
public sealed record ReminderHistoryQuery(
    Guid? InvoiceId = null,
    ReminderLevelDto? Level = null,
    ReminderOutcomeDto? Outcome = null,
    ReminderChannelDto? Channel = null,
    DateOnly? From = null,
    DateOnly? To = null,
    string? Search = null,
    ReminderSortDto Sort = ReminderSortDto.Date,
    bool Descending = true,
    int Page = 1,
    int PageSize = 50);

/// <summary>
/// Page Relances (vague 7). Lecture : <c>DossierLire</c> ; envoi et relance notée : <c>FactureEmettre</c> ; scénario et
/// envoi automatique : <c>CatalogueGerer</c>. Les e-mails partent par le port e-mail du serveur (MailPit dans la démo).
/// </summary>
public interface IRemindersGateway
{
    Task<OperationResult<ReminderOverviewResponse>> GetOverviewAsync(CancellationToken cancellationToken);

    Task<OperationResult<ReminderPreviewResponse>> GetPreviewAsync(Guid invoiceId, CancellationToken cancellationToken);

    Task<OperationResult<PaymentReminderResponse>> SendAsync(Guid invoiceId, ReminderChannelDto channel, CancellationToken cancellationToken);

    Task<OperationResult<RunRemindersResponse>> RunDueAsync(CancellationToken cancellationToken);

    Task<OperationResult<PagedResponse<PaymentReminderResponse>>> ListHistoryAsync(ReminderHistoryQuery query, CancellationToken cancellationToken);

    Task<OperationResult<ReminderSettingsResponse>> UpdateSettingsAsync(UpdateReminderSettingsRequest request, CancellationToken cancellationToken);
}
