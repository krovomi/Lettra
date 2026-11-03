using Lettra.Domain.Decisions;

namespace Lettra.Domain.Tests.Decisions;

/// <summary>
/// Une décision à prendre : proposée par une règle, remise à plus tard, acceptée, ignorée, réglée d'elle-même, et ce qui
/// la fait revenir ou non quand la règle la propose encore.
/// </summary>
public sealed class DecisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 3);
    private static readonly Guid User = Guid.CreateVersion7();

    private static DecisionProposal Proposal(DecisionUrgency urgency = DecisionUrgency.Normal, string subject = "facture-1:Friendly", DecisionAction? action = null) =>
        new("relance.due", subject, DecisionAudience.Entreprise, urgency, 1_200m, Today, new Dictionary<string, string> { ["invoice"] = "F-2026-0042" }, "reminders", action);

    [Fact]
    public void Une_proposition_devient_une_decision_a_decider_avec_ses_faits()
    {
        Decision decision = Decision.Propose(Proposal(action: new DecisionAction("relance.envoyer", "facture-1")), 2, Now);

        decision.Status.Should().Be(DecisionStatus.Proposed);
        decision.IsOpen.Should().BeTrue();
        decision.HasAction.Should().BeTrue();
        decision.Kind.Should().Be("relance.due");
        decision.Subject.Should().Be("facture-1:Friendly");
        decision.Audience.Should().Be(DecisionAudience.Entreprise);
        decision.Impact.Should().Be(1_200m);
        decision.DueOn.Should().Be(Today);
        decision.Facts.Should().ContainKey("invoice").WhoseValue.Should().Be("F-2026-0042");
        decision.Link.Should().Be("reminders");
        decision.ActionKind.Should().Be("relance.envoyer");
        decision.ActionPayload.Should().Be("facture-1");
        decision.RuleVersion.Should().Be(2);
        decision.ProposedAt.Should().Be(Now);
        decision.SeenAt.Should().Be(Now);
        decision.ClosedAt.Should().BeNull();
        decision.DecidedBy.Should().BeNull();
        decision.Id.ToString().Should().Be(decision.Id.Value.ToString());
    }

    [Fact]
    public void Proposee_de_nouveau_elle_prend_les_nouveaux_faits_et_revient_apres_plus_tard_ou_reglee()
    {
        Decision decision = Decision.Propose(Proposal(), 1, Now);
        decision.Refresh(Proposal(DecisionUrgency.High), 2, Today, Now.AddHours(1));
        decision.Urgency.Should().Be(DecisionUrgency.High);
        decision.RuleVersion.Should().Be(2);
        decision.SeenAt.Should().Be(Now.AddHours(1));

        decision.Snooze(Today.AddDays(7), Today, User).IsSuccess.Should().BeTrue();
        decision.Refresh(Proposal(), 2, Today.AddDays(6), Now.AddDays(6));
        decision.Status.Should().Be(DecisionStatus.Snoozed, "pas avant le jour choisi");
        decision.StatusOn(Today.AddDays(7)).Should().Be(DecisionStatus.Proposed, "le jour venu, elle compte comme à décider avant même l'actualisation");
        decision.Refresh(Proposal(), 2, Today.AddDays(7), Now.AddDays(7));
        decision.Status.Should().Be(DecisionStatus.Proposed);
        decision.SnoozedUntil.Should().BeNull();

        decision.Resolve(Now.AddDays(8));
        decision.Status.Should().Be(DecisionStatus.Resolved);
        decision.ClosedAt.Should().Be(Now.AddDays(8));
        decision.Refresh(Proposal(), 2, Today.AddDays(9), Now.AddDays(9));
        decision.Status.Should().Be(DecisionStatus.Proposed, "la situation est revenue");
        decision.ClosedAt.Should().BeNull();
    }

    [Fact]
    public void Acceptee_ou_ignoree_elle_reste_close_meme_si_la_regle_la_propose_encore()
    {
        Decision accepted = Decision.Propose(Proposal(), 1, Now);
        Decision dismissed = Decision.Propose(Proposal(), 1, Now);

        accepted.Accept(User, Now.AddMinutes(5)).IsSuccess.Should().BeTrue();
        dismissed.Dismiss(User, Now.AddMinutes(5)).IsSuccess.Should().BeTrue();
        accepted.Refresh(Proposal(DecisionUrgency.Critical), 1, Today, Now.AddDays(1));
        dismissed.Refresh(Proposal(DecisionUrgency.Critical), 1, Today, Now.AddDays(1));
        accepted.Resolve(Now.AddDays(2));

        accepted.Status.Should().Be(DecisionStatus.Accepted);
        accepted.DecidedBy.Should().Be(User);
        accepted.ClosedAt.Should().Be(Now.AddMinutes(5));
        accepted.Urgency.Should().Be(DecisionUrgency.Normal, "une décision close ne change plus");
        dismissed.Status.Should().Be(DecisionStatus.Dismissed);
        accepted.Accept(User, Now).Errors.Should().ContainSingle().Which.Code.Should().Be(DecisionErrors.NotOpenCode);
        accepted.Dismiss(User, Now).Errors.Should().ContainSingle().Which.Code.Should().Be(DecisionErrors.NotOpenCode);
        accepted.Snooze(Today.AddDays(1), Today, User).Errors.Should().ContainSingle().Which.Code.Should().Be(DecisionErrors.NotOpenCode);
        accepted.Restore().Errors.Should().ContainSingle().Which.Code.Should().Be(DecisionErrors.NotRestorableCode);
    }

    [Fact]
    public void Une_decision_ignoree_ou_remise_a_plus_tard_revient_dans_la_liste()
    {
        Decision dismissed = Decision.Propose(Proposal(), 1, Now);
        Decision snoozed = Decision.Propose(Proposal(), 1, Now);
        dismissed.Dismiss(User, Now);
        snoozed.Snooze(Today.AddDays(30), Today, User);

        dismissed.Restore().IsSuccess.Should().BeTrue();
        snoozed.Restore().IsSuccess.Should().BeTrue();

        dismissed.Status.Should().Be(DecisionStatus.Proposed);
        dismissed.ClosedAt.Should().BeNull();
        dismissed.DecidedBy.Should().BeNull();
        snoozed.Status.Should().Be(DecisionStatus.Proposed);
        snoozed.SnoozedUntil.Should().BeNull();
        Decision.Propose(Proposal(), 1, Now).Restore().Errors.Should().ContainSingle().Which.Code.Should().Be(DecisionErrors.NotRestorableCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(367)]
    public void Plus_tard_va_de_demain_a_un_an(int days)
    {
        Decision decision = Decision.Propose(Proposal(), 1, Now);

        decision.Snooze(Today.AddDays(days), Today, User).Errors.Should().ContainSingle()
            .Which.Should().Match<Lettra.Domain.Common.Error>(e => e.Code == DecisionErrors.SnoozeDateInvalidCode && e.Target == "SnoozeUntil");
        decision.Snooze(Today.AddDays(Decision.MaxSnoozeDays), Today, User).IsSuccess.Should().BeTrue();
        decision.SnoozedUntil.Should().Be(Today.AddDays(Decision.MaxSnoozeDays));
        decision.DecidedBy.Should().Be(User);
    }

    [Fact]
    public void Close_depuis_plus_de_treize_mois_elle_est_a_effacer()
    {
        Decision decision = Decision.Propose(Proposal(), 1, Now);
        decision.IsExpired(Now.AddYears(5)).Should().BeFalse("ouverte, elle reste");

        decision.Resolve(Now);

        decision.IsExpired(Now.AddMonths(Decision.RetentionMonths)).Should().BeFalse();
        decision.IsExpired(Now.AddMonths(Decision.RetentionMonths).AddDays(1)).Should().BeTrue();
    }

    [Fact]
    public void Une_regle_qui_propose_n_importe_quoi_est_une_erreur_de_programmation()
    {
        Decision decision = Decision.Propose(Proposal(), 1, Now);
        Dictionary<string, string> tooMany = Enumerable.Range(0, Decision.MaxFacts + 1).ToDictionary(i => $"f{i}", i => "x");

        FluentActions.Invoking(() => Decision.Propose(null!, 1, Now)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => Decision.Propose(Proposal() with { Kind = " " }, 1, Now)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Decision.Propose(Proposal() with { Subject = new string('x', Decision.MaxSubjectLength + 1) }, 1, Now)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Decision.Propose(Proposal() with { Link = "" }, 1, Now)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Decision.Propose(Proposal() with { Facts = tooMany }, 1, Now)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Decision.Propose(Proposal() with { Facts = new Dictionary<string, string> { ["x"] = new string('a', 201) } }, 1, Now)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Decision.Propose(Proposal(action: new DecisionAction("", "x")), 1, Now)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Decision.Propose(Proposal() with { Urgency = (DecisionUrgency)9 }, 1, Now)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => decision.Refresh(Proposal(subject: "autre"), 1, Today, Now)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Le_cadran_prepare_par_defaut_et_garde_qui_l_a_regle()
    {
        DecisionPolicy policy = DecisionPolicy.Default();

        policy.Id.Should().Be(DecisionPolicy.SingletonId);
        policy.Level.Should().Be(AutonomyLevel.Prepare);
        policy.RefreshedAt.Should().BeNull();
        policy.SetLevel(AutonomyLevel.Show, User, Now).IsSuccess.Should().BeTrue();
        policy.Level.Should().Be(AutonomyLevel.Show);
        policy.UpdatedBy.Should().Be(User);
        policy.UpdatedAt.Should().Be(Now);
        policy.SetLevel((AutonomyLevel)7, User, Now).Errors.Should().ContainSingle().Which.Code.Should().Be(DecisionErrors.LevelInvalidCode);
        policy.MarkRefreshed(Now);
        policy.RefreshedAt.Should().Be(Now);
    }
}
