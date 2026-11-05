using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Reminders;
using Lettra.Domain.Common;
using Lettra.Domain.Decisions;
using Lettra.Domain.Reminders;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Banking;
using Lettra.Exports.Application.Reminders;

namespace Lettra.Exports.Tests.Reminders;

/// <summary>
/// « Décisions à prendre » côté relances : une décision par relance due aujourd'hui, prête à envoyer quand le client a
/// une adresse et qu'un serveur d'envoi est branché ; rien quand l'envoi automatique s'en charge.
/// </summary>
public sealed partial class PaymentReminderServiceTests
{
    [Fact]
    public async Task Chaque_relance_due_devient_une_decision_prete_a_envoyer_quand_c_est_possible()
    {
        ReceivableDocument friendly = Add("F-1", Durand, 20, total: 1500m, paid: 300m);
        ReceivableDocument final = Add("F-2", Berlin, 35);
        _f.Reminders.Seed(final, ReminderLevel.Reminder, Today.AddDays(-10));
        ReceivableDocument noEmail = Add("F-3", Dupont, 20);
        Add("F-4", Durand, 1);

        IReadOnlyList<DecisionProposal> proposals = await new ReminderDecisionRule(_f.Service).ProposeAsync(Today, default);

        proposals.Select(p => (p.Subject, p.Urgency, p.Impact, p.Action?.Payload)).Should().BeEquivalentTo([
            ($"{final.InvoiceId}:{ReminderLevelDto.Final}", DecisionUrgency.High, 1200m, final.InvoiceId.Value.ToString()),
            ($"{friendly.InvoiceId}:{ReminderLevelDto.Friendly}", DecisionUrgency.Normal, 1200m, friendly.InvoiceId.Value.ToString()),
            ($"{noEmail.InvoiceId}:{ReminderLevelDto.Friendly}", DecisionUrgency.Normal, 1200m, null),
        ]);
        proposals.Should().AllSatisfy(p => p.Should().Match<DecisionProposal>(d =>
            d.Kind == ReminderDecisionRule.RuleKind && d.DueOn == Today && d.Link == "reminders" && d.Audience == DecisionAudience.Entreprise));
        proposals.Single(p => p.Subject.EndsWith(":Final", StringComparison.Ordinal)).Facts.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["invoice"] = "F-2",
            ["customer"] = "Berlin GmbH",
            ["days"] = "35",
            ["level"] = "@ReminderLevel.Final",
        });
        proposals.Should().AllSatisfy(p => p.Action?.Kind.Should().Be(SendReminderDecisionAction.ActionKindName));
    }

    [Fact]
    public async Task Sans_serveur_d_envoi_la_decision_mene_a_l_ecran_et_avec_l_envoi_automatique_il_n_y_en_a_pas()
    {
        Add("F-1", Durand, 20);
        ReminderDecisionRule rule = new(_f.Service);
        _f.Email.IsConfigured = false;

        IReadOnlyList<DecisionProposal> manual = await rule.ProposeAsync(Today, default);
        ReminderSettings settings = ReminderSettings.Default();
        settings.Update(true, ReminderSettings.DefaultDelays, ReminderSettings.DefaultMinimumGapDays, ReminderPrevention.Default, Guid.Empty, Now);
        await _f.Settings.AddAsync(settings, default);
        IReadOnlyList<DecisionProposal> automatic = await rule.ProposeAsync(Today, default);

        manual.Single().Action.Should().BeNull();
        automatic.Should().BeEmpty("la passe quotidienne envoie déjà les relances");
    }

    [Fact]
    public async Task Accepter_envoie_la_relance_par_la_commande_de_l_ecran_des_relances()
    {
        DossierId dossier = new(Guid.NewGuid());
        Guid invoice = Guid.NewGuid();
        Commands commands = new();
        SendReminderDecisionAction action = new(commands);

        Result sent = await action.ExecuteAsync(dossier, invoice.ToString(), default);
        commands.Refusal = ReminderErrors.CustomerEmailMissing;
        Result refused = await action.ExecuteAsync(dossier, invoice.ToString(), default);
        Result unreadable = await action.ExecuteAsync(dossier, "pas un identifiant", default);

        sent.IsSuccess.Should().BeTrue();
        refused.Errors.Single().Code.Should().Be(ReminderErrors.CustomerEmailMissingCode);
        unreadable.Errors.Single().Code.Should().Be(DecisionErrors.ActionUnavailableCode);
        commands.Sent.Should().HaveCount(2).And.AllBeEquivalentTo(new SendReminderCommand(dossier, invoice, new SendReminderRequest(ReminderChannelDto.Email)));
        action.Reversible.Should().BeFalse("un e-mail parti ne se reprend pas");
    }

    private sealed class Commands : ICommandDispatcher
    {
        public List<object> Sent { get; } = [];

        public Error? Refusal { get; set; }

        public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken)
        {
            Sent.Add(command);
            object result = Refusal is { } error
                ? Result<PaymentReminderResponse>.Failure(error)
                : Result<PaymentReminderResponse>.Success(null!);
            return Task.FromResult((TResult)result);
        }
    }
}
