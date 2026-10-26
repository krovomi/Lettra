using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.Vigilance;

namespace Lettra.Domain.Tests.Vigilance;

public sealed class SubcontractorAttestationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 26);
    private static readonly SupplierId Supplier = SupplierId.New();

    private static SubcontractorAttestation.Draft Draft(decimal amount = 12_000m, DateOnly? issued = null, string? code = "ab12 cd34 ef56", DateOnly? verified = null) =>
        new(amount, issued ?? new DateOnly(2026, 9, 15), code, verified ?? new DateOnly(2026, 9, 20));

    private static AttestationStatus Status(SubcontractorAttestation.Draft draft, DateOnly? on = null) =>
        SubcontractorAttestation.Create(Supplier, draft, Today, Now).Value.StatusOn(on ?? Today);

    [Fact]
    public void Une_attestation_verifiee_vaut_six_mois_et_s_annonce_trente_jours_avant()
    {
        SubcontractorAttestation attestation = SubcontractorAttestation.Create(Supplier, Draft(), Today, Now).Value;

        attestation.Id.Should().Be(Supplier);
        attestation.SecurityCode.Should().Be("AB12CD34EF56", "espaces retirés, en majuscules");
        attestation.IsRequired.Should().BeTrue();
        attestation.ValidUntil.Should().Be(new DateOnly(2027, 3, 14));
        attestation.UpdatedAt.Should().Be(Now);
        attestation.StatusOn(Today).Should().Be(AttestationStatus.Valid);
        attestation.StatusOn(new DateOnly(2027, 2, 12)).Should().Be(AttestationStatus.Valid);
        attestation.StatusOn(new DateOnly(2027, 2, 13)).Should().Be(AttestationStatus.ExpiringSoon);
        attestation.StatusOn(new DateOnly(2027, 3, 14)).Should().Be(AttestationStatus.ExpiringSoon);
        attestation.StatusOn(new DateOnly(2027, 3, 15)).Should().Be(AttestationStatus.Expired);
    }

    [Fact]
    public void Sous_le_seuil_rien_n_est_exige_au_dela_il_faut_une_attestation_verifiee()
    {
        Status(Draft(amount: 4_999.99m, code: null) with { IssuedOn = null, VerifiedOn = null }).Should().Be(AttestationStatus.NotRequired);
        Status(new SubcontractorAttestation.Draft(5_000m, null, null, null)).Should().Be(AttestationStatus.Missing);
        Status(Draft() with { VerifiedOn = null }).Should().Be(AttestationStatus.NotVerified);
        Status(Draft(), new DateOnly(2027, 4, 1)).Should().Be(AttestationStatus.Expired, "même vérifiée, elle a plus de six mois");

        SubcontractorAttestation.BlocksPayment(AttestationStatus.Missing).Should().BeTrue();
        SubcontractorAttestation.BlocksPayment(AttestationStatus.Expired).Should().BeTrue();
        SubcontractorAttestation.BlocksPayment(AttestationStatus.NotVerified).Should().BeTrue();
        SubcontractorAttestation.BlocksPayment(AttestationStatus.ExpiringSoon).Should().BeFalse();
        SubcontractorAttestation.BlocksPayment(AttestationStatus.Valid).Should().BeFalse();
        SubcontractorAttestation.BlocksPayment(AttestationStatus.NotRequired).Should().BeFalse();
    }

    [Theory]
    [InlineData(-1, AttestationErrors.AmountInvalidCode)]
    [InlineData(100_000_001, AttestationErrors.AmountInvalidCode)]
    [InlineData(1_000.555, AttestationErrors.AmountInvalidCode)]
    public void Un_montant_negatif_trop_grand_ou_au_millieme_est_refuse(double amount, string code) =>
        SubcontractorAttestation.Create(Supplier, Draft((decimal)amount), Today, Now).Errors.Should().ContainSingle().Which.Code.Should().Be(code);

    [Fact]
    public void Dates_futures_code_illisible_et_verification_incoherente_sont_refuses()
    {
        Codes(Draft(issued: Today.AddDays(1), verified: Today)).Should().Equal(AttestationErrors.DateInFutureCode, AttestationErrors.VerifiedBeforeIssuedCode);
        Codes(Draft(verified: Today.AddDays(1))).Should().Equal(AttestationErrors.DateInFutureCode);
        Codes(Draft(code: "ABC123")).Should().Equal(AttestationErrors.CodeInvalidCode);
        Codes(Draft(code: "ABCD-1234-EFGH")).Should().Equal(AttestationErrors.CodeInvalidCode);
        Codes(Draft(code: new string('A', 21))).Should().Equal(AttestationErrors.CodeInvalidCode);
        Codes(Draft(code: "  ")).Should().Equal(AttestationErrors.VerificationNeedsCodeCode);
        Codes(Draft() with { IssuedOn = null }).Should().Equal(AttestationErrors.VerificationNeedsCodeCode);
        Codes(Draft(issued: new DateOnly(2026, 9, 15), verified: new DateOnly(2026, 9, 14))).Should().Equal(AttestationErrors.VerifiedBeforeIssuedCode);

        static IEnumerable<string> Codes(SubcontractorAttestation.Draft draft) =>
            SubcontractorAttestation.Create(Supplier, draft, Today, Now).Errors.Select(e => e.Code);
    }

    [Fact]
    public void Une_mise_a_jour_refusee_ne_change_rien_et_une_mise_a_jour_acceptee_remplace_tout()
    {
        SubcontractorAttestation attestation = SubcontractorAttestation.Create(Supplier, Draft(), Today, Now).Value;
        DateTimeOffset later = Now.AddDays(1);

        Result refused = attestation.Update(Draft(amount: -5m), Today, later);
        refused.IsFailure.Should().BeTrue();
        attestation.ContractAmountExclTax.Should().Be(12_000m);
        attestation.UpdatedAt.Should().Be(Now);

        attestation.Update(new SubcontractorAttestation.Draft(3_000m, null, null, null), Today, later).IsSuccess.Should().BeTrue();
        attestation.ContractAmountExclTax.Should().Be(3_000m);
        attestation.IssuedOn.Should().BeNull();
        attestation.SecurityCode.Should().BeNull();
        attestation.VerifiedOn.Should().BeNull();
        attestation.ValidUntil.Should().BeNull();
        attestation.UpdatedAt.Should().Be(later);
        attestation.StatusOn(Today).Should().Be(AttestationStatus.NotRequired);
        FluentActions.Invoking(() => attestation.Update(null!, Today, later)).Should().Throw<ArgumentNullException>();
    }
}
