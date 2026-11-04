using Lettra.Contracts.Tax;
using Lettra.Domain.Decisions;
using Lettra.Exports.Application.Tax;

namespace Lettra.Exports.Tests.Tax;

/// <summary>
/// « Décisions à prendre » côté seuils : une décision par seuil proche (franchi, ou prévu dans les trois mois), critique
/// quand la TVA change dès maintenant ; le sujet porte la période, pour qu'une décision ignorée revienne l'année suivante.
/// </summary>
public sealed class ThresholdDecisionTests
{
    private static readonly DateOnly YearStart = new(2026, 1, 1);

    private static ThresholdWatchResponse Row(
        string code, ThresholdFamilyDto family, ThresholdTierDto tier, ThresholdStatusDto status, bool soon,
        DateOnly? crossedOn = null, DateOnly? expectedOn = null, decimal excess = 0m, Guid? party = null, string? partyName = null) =>
        new(code, family, tier, party is { } id ? $"{code}-{id}" : code, party, partyName, 41_250m, ThresholdBasisDto.ExclTax, YearStart, new DateOnly(2026, 12, 31),
            42_000m, 48_000m, excess, status, crossedOn, expectedOn, null, soon, "CGI");

    [Fact]
    public void Chaque_seuil_proche_devient_une_decision_critique_quand_la_TVA_change_tout_de_suite()
    {
        Guid supplier = Guid.NewGuid();
        ThresholdsResponse view = new(new DateOnly(2026, 10, 19), 90, 3, false,
        [
            Row("tva.franchise.services.majore", ThresholdFamilyDto.VatFranchise, ThresholdTierDto.Increased, ThresholdStatusDto.Crossed, true,
                crossedOn: new DateOnly(2026, 10, 2), excess: 750m),
            Row("vigilance.sous-traitant", ThresholdFamilyDto.SubcontractorVigilance, ThresholdTierDto.Simple, ThresholdStatusDto.Crossed, true,
                crossedOn: new DateOnly(2026, 9, 1), party: supplier, partyName: "Plâtrerie Dupuis"),
            Row("tva.franchise.services.base", ThresholdFamilyDto.VatFranchise, ThresholdTierDto.Base, ThresholdStatusDto.Projected, true,
                expectedOn: new DateOnly(2026, 11, 20)),
            Row("taille.micro", ThresholdFamilyDto.CompanySize, ThresholdTierDto.Simple, ThresholdStatusDto.Far, false),
            Row("tva.trimestrielle.base", ThresholdFamilyDto.VatQuarterlyReturn, ThresholdTierDto.Base, ThresholdStatusDto.Crossed, true),
        ]);

        IReadOnlyList<DecisionProposal> proposals = ThresholdDecisionRule.Proposals(view);

        proposals.Select(p => (p.Subject, p.Urgency, p.Impact, p.DueOn)).Should().Equal(
            ("tva.franchise.services.majore:2026-01-01", DecisionUrgency.Critical, 750m, new DateOnly(2026, 10, 2)),
            ($"vigilance.sous-traitant-{supplier}:2026-01-01", DecisionUrgency.High, null, new DateOnly(2026, 9, 1)),
            ("tva.franchise.services.base:2026-01-01", DecisionUrgency.Normal, null, new DateOnly(2026, 11, 20)),
            ("tva.trimestrielle.base:2026-01-01", DecisionUrgency.High, null, null));
        proposals[0].Facts.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["what"] = "@Thresholds.Code.tva.franchise.services.majore",
            ["state"] = "@Decision.seuil.proche.State.Crossed",
            ["on"] = "2026-10-02",
            ["change"] = "@Thresholds.Change.tva.franchise.services.majore",
            ["basis"] = "@Thresholds.Basis.ExclTax",
            ["thresholdAmount"] = "41250.00",
            ["toDateAmount"] = "42000.00",
            ["projectedAmount"] = "48000.00",
        });
        proposals[1].Facts.Should().Contain("what", "@Decision.seuil.proche.What.SubcontractorVigilance").And.Contain("party", "Plâtrerie Dupuis");
        proposals[3].Facts.Should().Contain("state", "@Thresholds.Status.Crossed").And.NotContainKey("on");
        proposals.Should().AllSatisfy(p => p.Should().Match<DecisionProposal>(d =>
            d.Kind == ThresholdDecisionRule.RuleKind && d.Audience == DecisionAudience.Entreprise && d.Link == "tax/thresholds" && d.Action == null));
    }
}
