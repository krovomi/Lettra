using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Common;
using Lettra.Contracts.Reminders;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Exports.Application.Reminders;

/// <summary>L'écran Relances : les factures échues non soldées, leur prochain niveau, et le scénario du dossier.</summary>
public sealed record GetReminderOverviewQuery(DossierId Dossier) : IQuery<Result<ReminderOverviewResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Le texte de la prochaine relance d'une facture, à relire avant l'envoi.</summary>
public sealed record GetReminderPreviewQuery(DossierId Dossier, Guid InvoiceId) : IQuery<Result<ReminderPreviewResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>
/// Envoyer la prochaine relance d'une facture par e-mail, ou noter qu'elle est partie autrement (courrier, téléphone).
/// Un envoi en échec est gardé dans l'historique et répond <c>Email.SendFailed</c>.
/// </summary>
public sealed record SendReminderCommand(DossierId Dossier, Guid InvoiceId, SendReminderRequest Request)
    : ICommand<Result<PaymentReminderResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureEmettre;
}

/// <summary>Envoyer maintenant par e-mail toutes les relances dues aujourd'hui (même passe que le Worker).</summary>
public sealed record RunDueRemindersCommand(DossierId Dossier) : ICommand<Result<RunRemindersResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureEmettre;
}

/// <summary>L'historique des relances, filtré et trié, page par page.</summary>
public sealed record ListPaymentRemindersQuery(
    DossierId Dossier,
    Guid? InvoiceId = null,
    Guid? CustomerId = null,
    ReminderLevelDto? Level = null,
    ReminderOutcomeDto? Outcome = null,
    ReminderChannelDto? Channel = null,
    DateOnly? From = null,
    DateOnly? To = null,
    string? Search = null,
    ReminderSortDto Sort = ReminderSortDto.Date,
    bool Descending = true,
    int Page = 1,
    int PageSize = 50)
    : IQuery<Result<PagedResponse<PaymentReminderResponse>>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

public sealed record GetReminderSettingsQuery(DossierId Dossier) : IQuery<Result<ReminderSettingsResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Régler le scénario de relance et l'envoi automatique : engage le dossier envers ses clients.</summary>
public sealed record UpdateReminderSettingsCommand(DossierId Dossier, UpdateReminderSettingsRequest Request)
    : ICommand<Result<ReminderSettingsResponse>>, IRequirePermission
{
    public Permission Permission => Permission.CatalogueGerer;
}
