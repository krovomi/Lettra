using Lettra.Application.Abstractions.Decisions;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Features.Decisions;
using Lettra.Contracts.Tax;
using Lettra.Domain.Common;
using Lettra.Domain.Decisions;

namespace Lettra.Exports.Application.Tax;

/// <summary>
/// Une échéance du calendrier fiscal est en retard, ou due dans les sept jours, et n'est pas marquée déposée. Le sujet
/// porte l'obligation et la fin de sa période : l'échéance suivante est une nouvelle décision. Sans calendrier (fiche
/// entreprise ou réglages fiscaux absents), rien n'est proposé.
/// </summary>
internal sealed class FiscalDeadlineDecisionRule(TaxDeclarations tax) : IDecisionRule
{
    public const string RuleKind = "fiscal.echeance";

    /// <summary>Une échéance plus proche que ce délai est urgente.</summary>
    public const int UrgentDays = 3;

    public string Kind => RuleKind;

    public int Version => 1;

    public Permission Permission => Permission.FecExporter;

    public async Task<IReadOnlyList<DecisionProposal>> ProposeAsync(DateOnly today, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<FiscalDeadlineResponse>> upcoming = await tax.UpcomingAsync(today, FiscalCalendar.DueSoonDays, cancellationToken).ConfigureAwait(false);
        return upcoming.IsFailure ? [] : Proposals(upcoming.Value, today);
    }

    /// <summary>Les échéances à décider, en fonction pure : ni déposées, ni lointaines.</summary>
    public static IReadOnlyList<DecisionProposal> Proposals(IEnumerable<FiscalDeadlineResponse> deadlines, DateOnly today) =>
        [.. deadlines.Where(d => d.State is FiscalDeadlineStateDto.Overdue or FiscalDeadlineStateDto.DueSoon or FiscalDeadlineStateDto.Upcoming)
            .Select(d => new DecisionProposal(
                RuleKind,
                $"{d.Obligation}:{DecisionFacts.Date(d.PeriodEnd)}",
                DecisionAudience.Cabinet,
                d.State == FiscalDeadlineStateDto.Overdue ? DecisionUrgency.Critical
                    : d.DueDate.DayNumber - today.DayNumber <= UrgentDays ? DecisionUrgency.High : DecisionUrgency.Normal,
                null,
                d.DueDate,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["obligation"] = DecisionFacts.Label($"Tax.Obligation.{d.Obligation}"),
                    ["periodEnd"] = DecisionFacts.Date(d.PeriodEnd),
                    ["dueOn"] = DecisionFacts.Date(d.DueDate),
                },
                "tax"))];
}
