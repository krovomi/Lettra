using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Features.Decisions;
using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;
using Lettra.Domain.Decisions;
using Lettra.Domain.Parties;
using Lettra.Domain.Vigilance;

namespace Lettra.Application.Tests.Decisions;

/// <summary>Les règles du cœur : IBAN à vérifier, attestations de vigilance, fiche anti-blanchiment.</summary>
public sealed class CoreDecisionRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 3);

    [Fact]
    public async Task Chaque_iban_a_verifier_est_une_decision_critique_si_le_pays_a_change()
    {
        IbanChangeResponse local = new(Guid.CreateVersion7(), Guid.CreateVersion7(), "Dupuis", "FR76…1", "FR76…2", false, Now.AddDays(-2), IbanChangeStatusDto.Pending, null, null, null);
        IbanChangeResponse abroad = local with { Id = Guid.CreateVersion7(), SupplierName = "Martin", CountryChanged = true };
        PendingIbans store = new([local, abroad]);
        IbanChangeDecisionRule rule = new(store);

        IReadOnlyList<DecisionProposal> proposals = await rule.ProposeAsync(Today, default);

        rule.Should().Match<IbanChangeDecisionRule>(r => r.Kind == "iban.a-verifier" && r.Version == 1 && r.Permission == Permission.TiersModifier);
        store.Filter.Should().Match<IbanChangeListFilter>(f => f.Status == IbanChangeStatusDto.Pending && f.Page == 1);
        proposals.Select(p => (p.Subject, p.Urgency)).Should().Equal((local.Id.ToString(), DecisionUrgency.High), (abroad.Id.ToString(), DecisionUrgency.Critical));
        proposals[0].Facts.Should().Equal(new Dictionary<string, string> { ["supplier"] = "Dupuis", ["changedOn"] = "2026-11-01" });
        proposals[0].Should().Match<DecisionProposal>(p => p.Link == "iban-changes" && p.Audience == DecisionAudience.Entreprise && p.Action == null);
    }

    [Fact]
    public async Task Un_fournisseur_a_ne_pas_payer_en_l_etat_ou_dont_l_attestation_expire_est_une_decision()
    {
        InMemoryAttestationStore store = new();
        SupplierId missing = Track(store, "Atelier Brun", 12_000m, null, false);
        SupplierId expired = Track(store, "Charpente Roux", 12_000m, new DateOnly(2026, 3, 1), true);
        SupplierId unverified = Track(store, "Dallage Petit", 12_000m, new DateOnly(2026, 10, 1), false);
        SupplierId soon = Track(store, "Enduits Blanc", 12_000m, new DateOnly(2026, 5, 20), true);
        Track(store, "Zinc et fils", 12_000m, new DateOnly(2026, 9, 1), true);
        Track(store, "Petit contrat", 3_000m, null, false);
        store.Suppliers.Add((SupplierId.New(), "Pas suivi"));
        AttestationDecisionRule rule = new(store);

        IReadOnlyList<DecisionProposal> proposals = await rule.ProposeAsync(Today, default);

        rule.Permission.Should().Be(Permission.TiersModifier);
        proposals.Select(p => (p.Subject, p.Urgency, p.DueOn)).Should().BeEquivalentTo(
        [
            ($"{missing}:aucune", DecisionUrgency.High, (DateOnly?)null),
            ($"{expired}:2026-08-31", DecisionUrgency.Critical, null),
            ($"{unverified}:2027-03-31", DecisionUrgency.High, null),
            ($"{soon}:2026-11-19", DecisionUrgency.Normal, new DateOnly(2026, 11, 19)),
        ]);
        proposals.Single(p => p.Subject.StartsWith(expired.ToString(), StringComparison.Ordinal)).Facts.Should().Equal(new Dictionary<string, string>
        {
            ["supplier"] = "Charpente Roux",
            ["status"] = "@Decision.attestation.a-renouveler.Status.Expired",
            ["contractAmount"] = "12000.00",
        });
    }

    [Fact]
    public async Task La_fiche_anti_blanchiment_absente_ou_a_revoir_est_une_decision_reservee_a_qui_la_remplit()
    {
        InMemoryVigilanceStore store = new();
        DueDiligenceDecisionRule rule = new(store);

        IReadOnlyList<DecisionProposal> none = await rule.ProposeAsync(Today, default);
        await store.AddAsync(File(RiskLevel.High, new DateOnly(2025, 10, 1)), default);
        IReadOnlyList<DecisionProposal> late = await rule.ProposeAsync(Today, default);
        IReadOnlyList<DecisionProposal> soon = await rule.ProposeAsync(new DateOnly(2026, 9, 15), default);
        IReadOnlyList<DecisionProposal> fine = await rule.ProposeAsync(new DateOnly(2026, 6, 1), default);

        rule.Permission.Should().Be(Permission.VigilanceGerer);
        none.Should().ContainSingle().Which.Should().Match<DecisionProposal>(p =>
            p.Subject == "fiche" && p.Urgency == DecisionUrgency.High && p.DueOn == null && p.Facts["status"] == "@Decision.lcb-ft.fiche.Status.Missing");
        late.Should().ContainSingle().Which.Should().Match<DecisionProposal>(p =>
            p.Subject == "revue:2026-10-01" && p.Urgency == DecisionUrgency.Critical && p.DueOn == new DateOnly(2026, 10, 1) && p.Audience == DecisionAudience.Cabinet);
        soon.Should().ContainSingle().Which.Should().Match<DecisionProposal>(p =>
            p.Subject == "revue:2026-10-01" && p.Urgency == DecisionUrgency.Normal && p.Facts["status"] == "@Decision.lcb-ft.fiche.Status.UpToDate");
        fine.Should().BeEmpty();
    }

    private static SupplierId Track(InMemoryAttestationStore store, string name, decimal amount, DateOnly? issued, bool verified)
    {
        SupplierId id = SupplierId.New();
        store.Suppliers.Add((id, name));
        SubcontractorAttestation attestation = SubcontractorAttestation.Create(
            id, new SubcontractorAttestation.Draft(amount, issued, issued is null ? null : "AB12CD34EF56", verified ? issued : null), Today, Now).Value;
        store.AddAsync(attestation, default).GetAwaiter().GetResult();
        return id;
    }

    private static ClientDueDiligence File(RiskLevel level, DateOnly reviewed) =>
        ClientDueDiligence.Create(
            new ClientDueDiligence.Draft(IdentityEvidence.Required, reviewed, [new("Claire Martin", 75m, OwnershipKind.Capital)], level, RiskFactors.None,
                level == RiskLevel.Standard ? null : "Motif.", reviewed, null),
            Guid.CreateVersion7(), reviewed, Now.AddYears(-1)).Value;

    private sealed class PendingIbans(IReadOnlyList<IbanChangeResponse> pending) : IIbanChangeReadStore
    {
        public IbanChangeListFilter? Filter { get; private set; }

        public Task<PagedResponse<IbanChangeResponse>> ListAsync(IbanChangeListFilter filter, CancellationToken cancellationToken)
        {
            Filter = filter;
            return Task.FromResult(new PagedResponse<IbanChangeResponse>(pending, filter.Page, filter.PageSize, pending.Count));
        }

        public Task<IReadOnlyList<IbanChangeResponse>> ForSupplierAsync(SupplierId supplier, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IbanChangeResponse>>([]);
    }
}
