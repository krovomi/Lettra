using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Decisions;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Features.Cabinet;
using Lettra.Application.Features.Parties;
using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;
using Lettra.Contracts.Vigilance;
using Lettra.Domain.Decisions;
using Lettra.Domain.SharedKernel;
using Lettra.Domain.Vigilance;

namespace Lettra.Application.Features.Decisions;

/// <summary>
/// Un IBAN de fournisseur a changé et n'est pas vérifié : le confirmer par un autre canal, ou le refuser, avant tout
/// paiement (fraude au faux fournisseur). Critique quand le pays de la banque a changé.
/// </summary>
internal sealed class IbanChangeDecisionRule(IIbanChangeReadStore changes) : IDecisionRule
{
    public const string RuleKind = "iban.a-verifier";

    /// <summary>Au-delà, l'écran des changements d'IBAN prend le relais.</summary>
    private const int MaxProposals = 50;

    public string Kind => RuleKind;

    public int Version => 1;

    public Permission Permission => Permission.TiersModifier;

    public async Task<IReadOnlyList<DecisionProposal>> ProposeAsync(DateOnly today, CancellationToken cancellationToken)
    {
        PagedResponse<IbanChangeResponse> pending = await changes.ListAsync(
            new IbanChangeListFilter(IbanChangeStatusDto.Pending, null, 1, MaxProposals, IbanChangeSortDto.ChangedAt, SortDirectionDto.Ascending), cancellationToken)
            .ConfigureAwait(false);
        return [.. pending.Items.Select(c => new DecisionProposal(
            RuleKind,
            c.Id.ToString(),
            DecisionAudience.Entreprise,
            c.CountryChanged ? DecisionUrgency.Critical : DecisionUrgency.High,
            null,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["supplier"] = c.SupplierName,
                ["changedOn"] = DecisionFacts.Date(LegalCalendar.DateOf(c.ChangedAt)),
            },
            "iban-changes"))];
    }
}

/// <summary>
/// Un fournisseur dont l'attestation de vigilance URSSAF est exigée mais absente, périmée ou non vérifiée (ne pas le payer
/// en l'état), ou expire dans les trente jours. Le sujet porte la fin de validité : une attestation renouvelée est une
/// nouvelle décision.
/// </summary>
internal sealed class AttestationDecisionRule(IAttestationStore store) : IDecisionRule
{
    public const string RuleKind = "attestation.a-renouveler";

    public string Kind => RuleKind;

    public int Version => 1;

    public Permission Permission => Permission.TiersModifier;

    public async Task<IReadOnlyList<DecisionProposal>> ProposeAsync(DateOnly today, CancellationToken cancellationToken)
    {
        IReadOnlyList<SupplierAttestationEntry> entries = await store.ListAsync(cancellationToken).ConfigureAwait(false);
        List<DecisionProposal> proposals = [];
        foreach (SupplierAttestationEntry entry in entries)
        {
            AttestationStatus status = AttestationOverview.StatusOf(entry.Attestation, today);
            DecisionUrgency? urgency = status switch
            {
                AttestationStatus.Expired => DecisionUrgency.Critical,
                AttestationStatus.Missing or AttestationStatus.NotVerified => DecisionUrgency.High,
                AttestationStatus.ExpiringSoon => DecisionUrgency.Normal,
                _ => null,
            };
            if (urgency is not { } level)
            {
                continue;
            }

            DateOnly? until = entry.Attestation?.ValidUntil;
            Dictionary<string, string> facts = new(StringComparer.Ordinal)
            {
                ["supplier"] = entry.SupplierName,
                ["status"] = DecisionFacts.Label($"Decision.{RuleKind}.Status.{status}"),
                ["contractAmount"] = DecisionFacts.Money(entry.Attestation?.ContractAmountExclTax ?? 0m),
            };
            proposals.Add(new DecisionProposal(
                RuleKind,
                $"{entry.SupplierId}:{(until is { } date ? DecisionFacts.Date(date) : "aucune")}",
                DecisionAudience.Entreprise,
                level,
                null,
                status == AttestationStatus.ExpiringSoon ? until : null,
                facts,
                "attestations"));
        }

        return proposals;
    }
}

/// <summary>
/// La fiche anti-blanchiment du client (LCB-FT) est absente, incomplète, à revoir, ou sa revue tombe dans les trente
/// jours. Réservée à qui la remplit : la fiche est confidentielle, et la décision ne dit pas le niveau de risque.
/// </summary>
internal sealed class DueDiligenceDecisionRule(IVigilanceStore store) : IDecisionRule
{
    public const string RuleKind = "lcb-ft.fiche";

    public string Kind => RuleKind;

    public int Version => 1;

    public Permission Permission => Permission.VigilanceGerer;

    public async Task<IReadOnlyList<DecisionProposal>> ProposeAsync(DateOnly today, CancellationToken cancellationToken)
    {
        ClientDueDiligence? file = await store.FindAsync(cancellationToken).ConfigureAwait(false);
        DueDiligenceStatusDto status = DueDiligenceOverview.StatusOf(file, today);
        (string Subject, DecisionUrgency Urgency)? decision = status switch
        {
            DueDiligenceStatusDto.Missing or DueDiligenceStatusDto.Incomplete => ("fiche", DecisionUrgency.High),
            DueDiligenceStatusDto.ReviewDue => ($"revue:{DecisionFacts.Date(file!.NextReviewOn)}", file.Level == RiskLevel.High ? DecisionUrgency.Critical : DecisionUrgency.High),
            DueDiligenceStatusDto.UpToDate when file!.NextReviewOn.DayNumber - today.DayNumber <= DueDiligenceOverview.ReviewNoticeDays =>
                ($"revue:{DecisionFacts.Date(file.NextReviewOn)}", DecisionUrgency.Normal),
            _ => null,
        };
        if (decision is not { } d)
        {
            return [];
        }

        Dictionary<string, string> facts = new(StringComparer.Ordinal) { ["status"] = DecisionFacts.Label($"Decision.{RuleKind}.Status.{status}") };
        DateOnly? reviewOn = status is DueDiligenceStatusDto.ReviewDue or DueDiligenceStatusDto.UpToDate ? file!.NextReviewOn : null;
        return [new DecisionProposal(RuleKind, d.Subject, DecisionAudience.Cabinet, d.Urgency, null, reviewOn, facts, "vigilance")];
    }
}
