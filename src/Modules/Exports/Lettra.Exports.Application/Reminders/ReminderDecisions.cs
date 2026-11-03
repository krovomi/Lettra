using Lettra.Application.Abstractions.Decisions;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Decisions;
using Lettra.Contracts.Reminders;
using Lettra.Domain.Common;
using Lettra.Domain.Decisions;
using Lettra.Domain.SharedKernel;

namespace Lettra.Exports.Application.Reminders;

/// <summary>
/// Une relance de facture est due aujourd'hui selon le scénario du dossier. L'action préparée l'envoie par e-mail quand
/// le client a une adresse et qu'un serveur d'envoi est branché ; sinon la décision mène à l'écran des relances. Rien
/// n'est proposé quand l'envoi automatique est réglé : la passe quotidienne s'en charge. Le sujet porte le niveau : la
/// relance suivante est une nouvelle décision.
/// </summary>
internal sealed class ReminderDecisionRule(PaymentReminderService reminders) : IDecisionRule
{
    public const string RuleKind = "relance.due";

    public string Kind => RuleKind;

    public int Version => 1;

    public Permission Permission => Permission.FactureEmettre;

    public async Task<IReadOnlyList<DecisionProposal>> ProposeAsync(DateOnly today, CancellationToken cancellationToken)
    {
        ReminderOverviewResponse overview = await reminders.OverviewAsync(cancellationToken).ConfigureAwait(false);
        if (overview.Settings.AutoSend)
        {
            return [];
        }

        return [.. overview.Invoices.Where(i => i is { DueToday: true, NextLevel: not null }).Select(i => new DecisionProposal(
            RuleKind,
            $"{i.InvoiceId}:{i.NextLevel}",
            DecisionAudience.Entreprise,
            i.NextLevel is ReminderLevelDto.Final or ReminderLevelDto.FormalNotice ? DecisionUrgency.High : DecisionUrgency.Normal,
            i.Outstanding,
            today,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["invoice"] = i.Number,
                ["customer"] = i.CustomerName,
                ["days"] = DecisionFacts.Number(i.DaysOverdue),
                ["level"] = DecisionFacts.Label($"ReminderLevel.{i.NextLevel}"),
            },
            "reminders",
            overview.Settings.EmailConfigured && i.CustomerEmail is not null
                ? new DecisionAction(SendReminderDecisionAction.ActionKindName, i.InvoiceId.ToString())
                : null))];
    }
}

/// <summary>
/// Envoie par e-mail la relance due d'une facture, par la commande de l'écran des relances (permission « émettre les
/// factures »). Un e-mail parti ne se reprend pas : l'action n'est pas réversible.
/// </summary>
internal sealed class SendReminderDecisionAction(ICommandDispatcher commands) : IDecisionActionHandler
{
    public const string ActionKindName = "relance.envoyer";

    public string ActionKind => ActionKindName;

    public bool Reversible => false;

    public async Task<Result> ExecuteAsync(DossierId dossier, string payload, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(payload, out Guid invoice))
        {
            return DecisionErrors.ActionUnavailable;
        }

        Result<PaymentReminderResponse> sent = await commands.SendAsync(
            new SendReminderCommand(dossier, invoice, new SendReminderRequest(ReminderChannelDto.Email)), cancellationToken).ConfigureAwait(false);
        return sent.IsFailure ? Result.Failure(sent.Errors) : Result.Success();
    }
}
