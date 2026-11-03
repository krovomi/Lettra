using Lettra.Contracts.Tax;
using Lettra.Domain.Decisions;
using Lettra.Exports.Application.Tax;

namespace Lettra.Exports.Tests.Tax;

/// <summary>« Décisions à prendre » côté fiscal : une décision par échéance ni déposée ni lointaine, la plus pressée en tête.</summary>
public sealed class FiscalDeadlineDecisionTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);

    private static FiscalDeadlineResponse Deadline(FiscalObligationDto obligation, int month, int day, FiscalDeadlineStateDto state) =>
        new(obligation, new DateOnly(2026, month - 1, 1), new DateOnly(2026, month - 1, 30), new DateOnly(2026, month, day), state, null);

    [Fact]
    public void Chaque_echeance_ouverte_devient_une_decision_plus_urgente_quand_elle_approche()
    {
        IReadOnlyList<DecisionProposal> proposals = FiscalDeadlineDecisionRule.Proposals(
        [
            Deadline(FiscalObligationDto.Des, 10, 10, FiscalDeadlineStateDto.Overdue),
            Deadline(FiscalObligationDto.VatReturnCa3, 10, 21, FiscalDeadlineStateDto.DueSoon),
            Deadline(FiscalObligationDto.CorporateTaxInstallment, 10, 26, FiscalDeadlineStateDto.DueSoon),
            Deadline(FiscalObligationDto.VatInstallment, 11, 15, FiscalDeadlineStateDto.Upcoming),
            Deadline(FiscalObligationDto.VatReturnCa3, 9, 15, FiscalDeadlineStateDto.Filed),
            Deadline(FiscalObligationDto.Des, 9, 10, FiscalDeadlineStateDto.Exempt),
        ], Today);

        proposals.Select(p => (p.Subject, p.Urgency, p.DueOn)).Should().Equal(
            ("Des:2026-09-30", DecisionUrgency.Critical, new DateOnly(2026, 10, 10)),
            ("VatReturnCa3:2026-09-30", DecisionUrgency.High, new DateOnly(2026, 10, 21)),
            ("CorporateTaxInstallment:2026-09-30", DecisionUrgency.Normal, new DateOnly(2026, 10, 26)),
            ("VatInstallment:2026-10-30", DecisionUrgency.Normal, new DateOnly(2026, 11, 15)));
        proposals[1].Facts.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["obligation"] = "@Tax.Obligation.VatReturnCa3",
            ["periodEnd"] = "2026-09-30",
            ["dueOn"] = "2026-10-21",
        });
        proposals.Should().AllSatisfy(p => p.Should().Match<DecisionProposal>(d =>
            d.Kind == FiscalDeadlineDecisionRule.RuleKind && d.Audience == DecisionAudience.Cabinet && d.Link == "tax" && d.Action == null && d.Impact == null));
    }
}
