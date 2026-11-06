using Lettra.Application.Abstractions.Decisions;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Decisions;
using Lettra.Contracts.Billing;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Decisions;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Billing;

/// <summary>
/// Un devis envoyé, sans réponse, arrive à sa fin de validité et n'a pas encore été relancé : le relancer. L'action
/// préparée envoie la relance proposée par l'écran (dans la langue du client) quand le client a une adresse et qu'un
/// serveur d'envoi est branché ; sinon la décision mène à l'écran des devis à relancer. Urgente la veille et le jour de
/// la fin. Le sujet porte la fin de validité : un devis prolongé puis de nouveau à relancer est une nouvelle décision.
/// </summary>
internal sealed class QuoteFollowUpDecisionRule(QuoteFollowUpService followUps) : IDecisionRule
{
    public const string RuleKind = "devis.a-relancer";

    /// <summary>Au-delà, l'écran des devis à relancer prend le relais.</summary>
    private const int MaxProposals = 50;

    public string Kind => RuleKind;

    public int Version => 1;

    public Permission Permission => Permission.DevisCreer;

    public async Task<IReadOnlyList<DecisionProposal>> ProposeAsync(DateOnly today, CancellationToken cancellationToken)
    {
        QuoteFollowUpOverviewResponse overview = await followUps.OverviewAsync(cancellationToken).ConfigureAwait(false);
        return Proposals(overview);
    }

    /// <summary>Une décision par devis à relancer, les plus proches de leur fin d'abord.</summary>
    public static IReadOnlyList<DecisionProposal> Proposals(QuoteFollowUpOverviewResponse overview)
    {
        ArgumentNullException.ThrowIfNull(overview);
        return [.. overview.Quotes.Where(q => q.Due).Take(MaxProposals).Select(q => new DecisionProposal(
            RuleKind,
            $"{q.QuoteId}:{DecisionFacts.Date(q.ValidUntil)}",
            DecisionAudience.Entreprise,
            q.DaysLeft <= 1 ? DecisionUrgency.High : DecisionUrgency.Normal,
            q.TotalExclTax,
            q.ValidUntil,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["quote"] = q.Number,
                ["customer"] = q.CustomerName,
                ["validUntil"] = DecisionFacts.Date(q.ValidUntil),
            },
            "quote-follow-ups",
            overview.Settings.EmailConfigured && q.CustomerEmail is not null
                ? new DecisionAction(SendQuoteFollowUpDecisionAction.ActionKindName, q.QuoteId.ToString())
                : null))];
    }
}

/// <summary>
/// Envoie par e-mail la relance proposée d'un devis, par la commande de l'écran des devis à relancer (permission
/// « créer des devis »). Un e-mail parti ne se reprend pas : l'action n'est pas réversible.
/// </summary>
internal sealed class SendQuoteFollowUpDecisionAction(ICommandDispatcher commands) : IDecisionActionHandler
{
    public const string ActionKindName = "devis.relancer";

    public string ActionKind => ActionKindName;

    public bool Reversible => false;

    public async Task<Result> ExecuteAsync(DossierId dossier, string payload, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(payload, out Guid quote))
        {
            return DecisionErrors.ActionUnavailable;
        }

        Result<QuoteFollowUpResponse> sent = await commands.SendAsync(
            new SendQuoteFollowUpCommand(dossier, new QuoteId(quote), new SendQuoteFollowUpRequest { Channel = QuoteFollowUpChannelDto.Email }), cancellationToken).ConfigureAwait(false);
        return sent.IsFailure ? Result.Failure(sent.Errors) : Result.Success();
    }
}
