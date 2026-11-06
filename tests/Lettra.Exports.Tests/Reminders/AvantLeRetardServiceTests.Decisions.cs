using Lettra.Application.Abstractions.Decisions;
using Lettra.Application.Abstractions.Identity;
using Lettra.Contracts.Reminders;
using Lettra.Domain.Decisions;
using Lettra.Domain.Reminders;
using Lettra.Exports.Application.Banking;
using Lettra.Exports.Application.Reminders;

namespace Lettra.Exports.Tests.Reminders;

/// <summary>
/// « Avant le retard » dans « Décisions à prendre » : chaque signal du jour devient une décision de son type, l'échéance
/// manquée en urgente ; l'envoi automatique ne laisse que l'échéancier à voir ; la relance de l'échéance manquée n'est
/// pas proposée une seconde fois par les relances dues.
/// </summary>
public sealed partial class AvantLeRetardServiceTests
{
    [Fact]
    public async Task Chaque_signal_du_jour_devient_une_decision_de_son_type()
    {
        ReceivableDocument coming = Add("F-10", Durand, 3);
        ReceivableDocument late = Add("F-20", Durand, -20);
        PaymentPlan plan = Plan(late);
        TodaysPaymentPreventionSignals signals = new(_f.Service);

        IReadOnlyList<DecisionProposal> beforeDue = await new BeforeDueDecisionRule(signals).ProposeAsync(Today, default);
        IReadOnlyList<DecisionProposal> missed = await new MissedInstallmentDecisionRule(signals).ProposeAsync(Today, default);
        IReadOnlyList<DecisionProposal> toReview = await new PaymentPlanToReviewDecisionRule(signals).ProposeAsync(Today, default);

        beforeDue.Select(p => (p.Kind, p.Subject, p.Urgency, p.Impact, p.DueOn)).Should().Equal(
            (PaymentPreventionSignals.BeforeDueKind, $"{coming.InvoiceId.Value}:2026-10-22", DecisionUrgency.Normal, 1200m, Today.AddDays(3)));
        missed.Select(p => (p.Kind, p.Subject, p.Urgency, p.Impact, p.DueOn)).Should().Equal(
            (PaymentPreventionSignals.MissedInstallmentKind, $"{late.InvoiceId.Value}:2026-10-15", DecisionUrgency.High, 600m, new DateOnly(2026, 10, 15)));
        toReview.Select(p => (p.Kind, p.Subject, p.Urgency, p.Impact, p.DueOn)).Should().Equal(
            (PaymentPreventionSignals.PlanToReviewKind, $"{plan.Id}:2026-10-15", DecisionUrgency.Normal, 1200m, new DateOnly(2026, 10, 15)));
        beforeDue.Concat(missed).Concat(toReview).Should().AllSatisfy(p => p.Should().Match<DecisionProposal>(d =>
            d.Link == "reminders" && d.Action == null && d.Audience == DecisionAudience.Entreprise));
        beforeDue.Single().Facts.Should().Equal(new Dictionary<string, string> { ["numero"] = "F-10", ["client"] = "Durand SARL", ["echeance"] = "2026-10-22" });
        new IDecisionRule[] { new BeforeDueDecisionRule(signals), new MissedInstallmentDecisionRule(signals), new PaymentPlanToReviewDecisionRule(signals) }
            .Should().AllSatisfy(r => r.Should().Match<IDecisionRule>(rule => rule.Permission == Permission.FactureEmettre && rule.Version == 1));
    }

    [Fact]
    public void Une_echeance_sans_date_garde_son_sujet_seul_et_les_autres_types_sont_laisses()
    {
        Guid invoice = Guid.NewGuid();
        PaymentPreventionSignal[] signals =
        [
            new(PaymentPreventionSignals.BeforeDueKind, invoice, null, null, new Dictionary<string, string>(), "/reminders"),
            new(PaymentPreventionSignals.PlanToReviewKind, Guid.NewGuid(), 10m, Today, new Dictionary<string, string>(), "/reminders"),
        ];

        IReadOnlyList<DecisionProposal> proposals = PaymentPreventionDecisionRule.Proposals(PaymentPreventionSignals.BeforeDueKind, DecisionUrgency.Normal, signals);

        proposals.Select(p => (p.Subject, p.DueOn, p.Impact)).Should().Equal((invoice.ToString(), null, null));
    }

    [Fact]
    public async Task Avec_l_envoi_automatique_seul_l_echeancier_a_voir_reste_a_decider()
    {
        _f.Settings.Open(autoSend: true);
        Add("F-10", Durand, 3);
        ReceivableDocument late = Add("F-20", Durand, -20);
        Plan(late);
        TodaysPaymentPreventionSignals signals = new(_f.Service);

        IReadOnlyList<DecisionProposal> beforeDue = await new BeforeDueDecisionRule(signals).ProposeAsync(Today, default);
        IReadOnlyList<DecisionProposal> missed = await new MissedInstallmentDecisionRule(signals).ProposeAsync(Today, default);
        IReadOnlyList<DecisionProposal> toReview = await new PaymentPlanToReviewDecisionRule(signals).ProposeAsync(Today, default);

        beforeDue.Should().BeEmpty("la passe quotidienne envoie le rappel");
        missed.Should().BeEmpty("la passe quotidienne relance l'échéance manquée");
        toReview.Should().ContainSingle();
    }

    [Fact]
    public async Task Les_trois_regles_lisent_les_signaux_une_seule_fois_par_actualisation()
    {
        ReceivableDocument coming = Add("F-10", Durand, 3);
        TodaysPaymentPreventionSignals signals = new(_f.Service);

        IReadOnlyList<DecisionProposal> first = await new BeforeDueDecisionRule(signals).ProposeAsync(Today, default);
        _f.Receivables.Documents.Remove(coming);
        IReadOnlyList<DecisionProposal> second = await new BeforeDueDecisionRule(signals).ProposeAsync(Today, default);
        IReadOnlyList<DecisionProposal> fresh = await new BeforeDueDecisionRule(new TodaysPaymentPreventionSignals(_f.Service)).ProposeAsync(Today, default);

        first.Should().ContainSingle();
        second.Should().BeEquivalentTo(first);
        fresh.Should().BeEmpty();
    }

    [Fact]
    public async Task La_relance_d_une_echeance_manquee_n_est_pas_proposee_deux_fois()
    {
        ReceivableDocument late = Add("F-20", Durand, -20);
        Plan(late);

        ReminderOverviewResponse overview = await _f.Service.OverviewAsync(default);
        IReadOnlyList<DecisionProposal> reminders = await new ReminderDecisionRule(_f.Service).ProposeAsync(Today, default);

        overview.Invoices.Should().Contain(i => i.DueToday && i.NextLevel == ReminderLevelDto.MissedInstallment, "l'échéance manquée est bien à relancer");
        reminders.Should().BeEmpty("elle est proposée par « Avant le retard »");
    }
}
