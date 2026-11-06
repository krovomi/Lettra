using Lettra.Application.Abstractions.Decisions;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Features.Decisions;
using Lettra.Contracts.Decisions;
using Lettra.Contracts.Insights;
using Lettra.Domain.Decisions;

namespace Lettra.Exports.Application.Insights.Prevention;

/// <summary>
/// Le radar de prévention passe « à appeler » : proposer au cabinet de prendre rendez-vous avec le dirigeant. Le sujet est
/// le mois : une décision ignorée revient le mois suivant si le dossier est toujours « à appeler ». Pas d'action préparée :
/// le courrier se relit et s'envoie depuis le radar, jamais tout seul (aucune décision automatique). Réservée à qui voit le
/// radar ; le compteur de la vue cabinet et Grafana ne la comptent pas (<see cref="DecisionKinds.CabinetOnly"/>).
/// </summary>
internal sealed class PreventionDecisionRule(PreventionRadarReader reader) : IDecisionRule
{
    public string Kind => DecisionKinds.PreventionMeeting;

    public int Version => 1;

    public Permission Permission => Permission.PreventionConsulter;

    public async Task<IReadOnlyList<DecisionProposal>> ProposeAsync(DateOnly today, CancellationToken cancellationToken) =>
        Proposals(await reader.ComputeAsync(today, cancellationToken).ConfigureAwait(false));

    /// <summary>Une proposition quand le dossier est « à appeler », aucune sinon.</summary>
    public static IReadOnlyList<DecisionProposal> Proposals(PreventionRadarResponse radar)
    {
        ArgumentNullException.ThrowIfNull(radar);
        if (radar.Level != PreventionLevelDto.Call)
        {
            return [];
        }

        PreventionSignalResponse[] active = [.. radar.Signals.Where(s => s.IsActive).OrderByDescending(s => s.State).ThenBy(s => s.Signal)];
        Dictionary<string, string> facts = new(StringComparer.Ordinal)
        {
            ["count"] = active.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (active.Length > 0)
        {
            facts["first"] = DecisionFacts.Label($"Prevention.Signal.{active[0].Signal}");
        }

        return
        [
            new DecisionProposal(
                DecisionKinds.PreventionMeeting,
                $"rendez-vous:{radar.Today.Year:D4}-{radar.Today.Month:D2}",
                DecisionAudience.Cabinet,
                DecisionUrgency.High,
                null,
                active.Min(s => s.DueOn),
                facts,
                "prevention"),
        ];
    }
}
