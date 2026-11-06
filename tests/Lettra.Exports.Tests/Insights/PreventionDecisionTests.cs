using Lettra.Application.Abstractions.Identity;
using Lettra.Contracts.Decisions;
using Lettra.Contracts.Insights;
using Lettra.Domain.Decisions;
using Lettra.Domain.Prevention;
using Lettra.Exports.Application.Insights.Prevention;

namespace Lettra.Exports.Tests.Insights;

/// <summary>
/// « Décisions à prendre » côté radar de prévention : une seule décision, quand le dossier passe « à appeler », pour
/// proposer un rendez-vous ; ni montant ni action préparée (rien ne part sans le cabinet) ; le sujet est le mois.
/// </summary>
public sealed class PreventionDecisionTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);

    private static PreventionSignalResponse Signal(PreventionSignalDto signal, PreventionSignalStateDto state, DateOnly? dueOn = null) =>
        new(signal, state, PreventionUnavailableDto.None, 1_000m, dueOn, [], [], [], []);

    private static PreventionRadarResponse Radar(PreventionLevelDto level, params PreventionSignalResponse[] signals) =>
        new(Today, level, signals.Count(s => s.IsActive), signals, PreventionRules.Describe(PreventionSettings.Default()));

    [Fact]
    public void Un_dossier_a_appeler_propose_un_rendez_vous_le_signal_le_plus_grave_en_tete()
    {
        PreventionRadarResponse radar = Radar(
            PreventionLevelDto.Call,
            Signal(PreventionSignalDto.PaymentDelay, PreventionSignalStateDto.Watch, Today.AddDays(30)),
            Signal(PreventionSignalDto.Treasury, PreventionSignalStateDto.Clear),
            Signal(PreventionSignalDto.CustomersInProceedings, PreventionSignalStateDto.Call, Today.AddDays(12)),
            Signal(PreventionSignalDto.PeerGap, PreventionSignalStateDto.NotEvaluated));

        DecisionProposal proposal = PreventionDecisionRule.Proposals(radar).Should().ContainSingle().Subject;

        proposal.Should().BeEquivalentTo(new DecisionProposal(
            DecisionKinds.PreventionMeeting,
            "rendez-vous:2026-11",
            DecisionAudience.Cabinet,
            DecisionUrgency.High,
            null,
            Today.AddDays(12),
            new Dictionary<string, string> { ["count"] = "2", ["first"] = "@Prevention.Signal.CustomersInProceedings" },
            "prevention"));
        proposal.Action.Should().BeNull("le courrier se relit et s'envoie depuis le radar");
    }

    [Theory]
    [InlineData(PreventionLevelDto.Calm)]
    [InlineData(PreventionLevelDto.Watch)]
    public void Un_dossier_calme_ou_a_surveiller_ne_propose_rien(PreventionLevelDto level) =>
        PreventionDecisionRule.Proposals(Radar(level, Signal(PreventionSignalDto.TopCustomer, PreventionSignalStateDto.Watch))).Should().BeEmpty();

    [Fact]
    public void La_regle_lit_le_radar_du_dossier_et_demande_le_droit_de_le_voir()
    {
        PreventionDecisionRule rule = new(null!);

        rule.Kind.Should().Be("prevention.rendez-vous");
        rule.Version.Should().Be(1);
        rule.Permission.Should().Be(Permission.PreventionConsulter);
        DecisionKinds.CabinetOnly.Should().Contain(rule.Kind);
        FluentActions.Invoking(() => PreventionDecisionRule.Proposals(null!)).Should().Throw<ArgumentNullException>();
    }
}
