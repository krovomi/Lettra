using Lettra.Application.Abstractions.Decisions;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Features.Decisions;
using Lettra.Contracts.Tax;
using Lettra.Domain.Decisions;

namespace Lettra.Exports.Application.Tax;

/// <summary>
/// Un seuil réglementaire est franchi, ou son franchissement est prévu dans les trois mois (franchise ou régime de TVA,
/// déclaration trimestrielle, catégorie, attestation de vigilance, DAS 2). Le sujet porte le seuil et le début de sa
/// période : une décision ignorée revient à la période suivante. Pas d'action : le geste est dans la fiche entreprise, les
/// réglages fiscaux ou chez le cabinet, que la décision mène à prévenir.
/// </summary>
internal sealed class ThresholdDecisionRule(ThresholdWatch thresholds) : IDecisionRule
{
    public const string RuleKind = "seuil.proche";

    public string Kind => RuleKind;

    public int Version => 1;

    public Permission Permission => Permission.FecExporter;

    public async Task<IReadOnlyList<DecisionProposal>> ProposeAsync(DateOnly today, CancellationToken cancellationToken) =>
        Proposals(await thresholds.ReadAsync(today, cancellationToken).ConfigureAwait(false));

    /// <summary>Les seuils proches, en fonction pure : critique quand la TVA change dès maintenant.</summary>
    public static IReadOnlyList<DecisionProposal> Proposals(ThresholdsResponse view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return [.. view.Rows.Where(r => r.Soon).Select(Proposal)];
    }

    private static DecisionProposal Proposal(ThresholdWatchResponse row)
    {
        DateOnly? on = ThresholdWatch.DueOn(row);
        Dictionary<string, string> facts = new(StringComparer.Ordinal)
        {
            // Le libellé du seuil ; celui d'un fournisseur ou d'un bénéficiaire porte son nom ({party}).
            ["what"] = DecisionFacts.Label(row.PartyName is null ? $"Thresholds.Code.{row.Code}" : $"Decision.{RuleKind}.What.{row.Family}"),
            ["state"] = DecisionFacts.Label(on is null ? $"Thresholds.Status.{row.Status}" : $"Decision.{RuleKind}.State.{row.Status}"),
            ["change"] = DecisionFacts.Label($"Thresholds.Change.{row.Code}"),
            ["basis"] = DecisionFacts.Label($"Thresholds.Basis.{row.Basis}"),
            ["thresholdAmount"] = DecisionFacts.Money(row.Threshold),
            ["toDateAmount"] = DecisionFacts.Money(row.ToDate),
            ["projectedAmount"] = DecisionFacts.Money(row.Projected),
        };
        if (row.PartyName is { } party)
        {
            facts["party"] = party;
        }

        if (on is { } date)
        {
            facts["on"] = DecisionFacts.Date(date);
        }

        return new DecisionProposal(
            RuleKind,
            $"{row.Subject}:{DecisionFacts.Date(row.PeriodStart)}",
            DecisionAudience.Entreprise,
            UrgencyOf(row),
            row.Excess > 0m ? row.Excess : null,
            on,
            facts,
            "tax/thresholds");
    }

    /// <summary>Critique quand un seuil de TVA à effet immédiat est franchi (la TVA des factures change dès maintenant).</summary>
    private static DecisionUrgency UrgencyOf(ThresholdWatchResponse row) => row switch
    {
        { Status: ThresholdStatusDto.Crossed, Tier: ThresholdTierDto.Increased, Family: ThresholdFamilyDto.VatFranchise or ThresholdFamilyDto.VatSimplifiedRegime or ThresholdFamilyDto.VatQuarterlyReturn }
            => DecisionUrgency.Critical,
        { Status: ThresholdStatusDto.Crossed } => DecisionUrgency.High,
        _ => DecisionUrgency.Normal,
    };
}
