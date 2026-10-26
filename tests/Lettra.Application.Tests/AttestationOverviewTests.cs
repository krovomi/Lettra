using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Features.Parties;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Vigilance;
using Lettra.Domain.Parties;
using Lettra.Domain.Vigilance;

namespace Lettra.Application.Tests;

/// <summary>L'écran « Attestations de vigilance » et son indicateur dans la vue cabinet : ordre, état, échéance, conversions.</summary>
public sealed class AttestationOverviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 26);

    private static SubcontractorAttestation Attestation(decimal amount = 12_000m, DateOnly? issued = null, bool verified = true) =>
        SubcontractorAttestation.Create(
            SupplierId.New(),
            new SubcontractorAttestation.Draft(amount, issued, issued is null ? null : "AB12CD34EF56", verified ? issued : null),
            Today,
            Now).Value;

    [Fact]
    public void La_vue_cabinet_compte_les_fournisseurs_a_ne_pas_payer_et_devient_critique_si_une_attestation_est_perimee()
    {
        SubcontractorAttestation valid = Attestation(issued: new DateOnly(2026, 9, 1));
        SubcontractorAttestation missing = Attestation();
        SubcontractorAttestation unverified = Attestation(issued: new DateOnly(2026, 10, 1), verified: false);
        SubcontractorAttestation expired = Attestation(issued: new DateOnly(2026, 3, 1));

        AttestationOverview.Indicator([valid, Attestation(3_000m)], Today).Should().BeNull("rien à faire");
        AttestationOverview.Indicator([valid, missing, unverified], Today).Should().Be(
            new DossierIndicator(DossierIndicatorKeys.SupplierAttestations, 2, IndicatorLevel.Warning, new DateOnly(2027, 3, 31)));
        AttestationOverview.Indicator([missing, expired], Today).Should().Be(
            new DossierIndicator(DossierIndicatorKeys.SupplierAttestations, 2, IndicatorLevel.Critical, new DateOnly(2026, 8, 31)));
        AttestationOverview.Indicator([valid], new DateOnly(2027, 2, 1)).Should().Be(
            new DossierIndicator(DossierIndicatorKeys.SupplierAttestations, 1, IndicatorLevel.Info, new DateOnly(2027, 2, 28)));
        FluentActions.Invoking(() => AttestationOverview.Indicator(null!, Today)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void L_ecran_montre_d_abord_ce_qui_bloque_un_paiement_puis_ce_qui_expire()
    {
        SupplierAttestationEntry[] entries =
        [
            new(SupplierId.New(), "Zinc et fils", Attestation(issued: new DateOnly(2026, 9, 1))),
            new(SupplierId.New(), "Atelier Brun", null),
            new(SupplierId.New(), "Béton Martin", Attestation()),
            new(SupplierId.New(), "Charpente Roux", Attestation(issued: new DateOnly(2026, 3, 1))),
            new(SupplierId.New(), "Dallage Petit", Attestation(issued: new DateOnly(2026, 10, 1), verified: false)),
            new(SupplierId.New(), "Enduits Blanc", Attestation(issued: new DateOnly(2026, 5, 10))),
            new(SupplierId.New(), "atelier Azur", Attestation(issued: new DateOnly(2026, 9, 1))),
        ];

        SupplierAttestationsResponse view = AttestationOverview.Build(entries, Today);

        view.Rows.Select(r => r.SupplierName).Should().Equal(
            "Charpente Roux", "Béton Martin", "Dallage Petit", "Enduits Blanc", "atelier Azur", "Zinc et fils", "Atelier Brun");
        view.Rows.Select(r => r.Status).Should().Equal(
            AttestationStatusDto.Expired, AttestationStatusDto.Missing, AttestationStatusDto.NotVerified, AttestationStatusDto.ExpiringSoon,
            AttestationStatusDto.Valid, AttestationStatusDto.Valid, AttestationStatusDto.NotRequired);
        view.Rows[^1].Should().Be(new SupplierAttestationRow(entries[1].SupplierId.Value, "Atelier Brun", null, null, null, null, null, AttestationStatusDto.NotRequired, null));
        view.Rows[0].Should().Be(new SupplierAttestationRow(entries[3].SupplierId.Value, "Charpente Roux", 12_000m, new DateOnly(2026, 3, 1), new DateOnly(2026, 8, 31),
            "AB12CD34EF56", new DateOnly(2026, 3, 1), AttestationStatusDto.Expired, 0));
        FluentActions.Invoking(() => AttestationOverview.Build(null!, Today)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => AttestationOverview.ToRow(null!, Today)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void La_demande_devient_le_brouillon_du_domaine()
    {
        SaveAttestationRequest request = new() { ContractAmountExclTax = 8_000m, IssuedOn = Today, SecurityCode = "X", VerifiedOn = Today };

        request.ToDraft().Should().Be(new SubcontractorAttestation.Draft(8_000m, Today, "X", Today));
        AttestationOverview.StatusOf(null, Today).Should().Be(AttestationStatus.NotRequired);
        FluentActions.Invoking(() => ((SaveAttestationRequest)null!).ToDraft()).Should().Throw<ArgumentNullException>();
    }
}
