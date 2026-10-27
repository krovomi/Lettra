using Lettra.Domain.Common;
using Lettra.Domain.Parties;

namespace Lettra.Domain.Tests.Parties;

public sealed class FeeBeneficiaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Year2025 = new(2025, 1, 1);
    private static readonly SupplierId Supplier = SupplierId.New();

    private static FeeBeneficiary.Draft Draft(decimal? outside = null, FeeNature nature = FeeNature.Fees, string profession = " Avocat ") =>
        new(nature, profession, outside is { } amount ? new FeeBeneficiary.YearAmount(Year2025, amount) : null);

    [Fact]
    public void Un_beneficiaire_garde_sa_nature_sa_profession_et_ce_qui_a_ete_verse_hors_releves_par_exercice()
    {
        FeeBeneficiary beneficiary = FeeBeneficiary.Create(Supplier, Draft(350.50m), Now).Value;

        beneficiary.Id.Should().Be(Supplier);
        beneficiary.Nature.Should().Be(FeeNature.Fees);
        beneficiary.Profession.Should().Be("Avocat");
        beneficiary.PaidOutsideFor(Year2025).Should().Be(350.50m);
        beneficiary.PaidOutsideFor(Year2025.AddYears(1)).Should().Be(0m);
        beneficiary.UpdatedAt.Should().Be(Now);

        beneficiary.Update(Draft() with { PaidOutside = new(Year2025.AddYears(1), 100m) }, Now).IsSuccess.Should().BeTrue();
        beneficiary.Update(Draft(500m, FeeNature.Commissions, "Agent commercial"), Now).IsSuccess.Should().BeTrue();
        beneficiary.PaidOutside.Should().HaveCount(2);
        beneficiary.PaidOutsideFor(Year2025).Should().Be(500m);
        beneficiary.Nature.Should().Be(FeeNature.Commissions);

        beneficiary.Update(Draft(0m), Now).IsSuccess.Should().BeTrue();
        beneficiary.PaidOutside.Should().ContainSingle().Which.FiscalYearStart.Should().Be(Year2025.AddYears(1), "zéro retire la somme notée");
        beneficiary.Update(Draft(0m), Now).IsSuccess.Should().BeTrue();
        beneficiary.PaidOutside.Should().ContainSingle();
    }

    [Fact]
    public void Le_seuil_est_depasse_au_dela_de_2400_euros_depuis_2024_et_de_1200_euros_avant()
    {
        DateOnly closed2024 = new(2024, 12, 31);
        DateOnly closed2023 = new(2023, 12, 31);
        FeeBeneficiary.MustBeDeclared(2_400m, closed2024).Should().BeFalse();
        FeeBeneficiary.MustBeDeclared(2_400.01m, closed2024).Should().BeTrue();
        FeeBeneficiary.ThresholdFor(closed2024).Should().Be(2_400m);
        FeeBeneficiary.MustBeDeclared(1_200m, closed2023).Should().BeFalse();
        FeeBeneficiary.MustBeDeclared(1_200.01m, closed2023).Should().BeTrue();
        FeeBeneficiary.ThresholdFor(closed2023).Should().Be(1_200m);
    }

    [Fact]
    public void Nature_inconnue_profession_vide_ou_trop_longue_et_montant_faux_sont_refuses()
    {
        Codes(Draft(nature: (FeeNature)42)).Should().Equal(FeeErrors.NatureInvalidCode);
        Codes(Draft(profession: "  ")).Should().Equal(FeeErrors.ProfessionRequiredCode);
        Codes(Draft(profession: new string('a', 61))).Should().Equal(FeeErrors.ProfessionTooLongCode);
        Codes(Draft(-1m)).Should().Equal(FeeErrors.AmountInvalidCode);
        Codes(Draft(10.001m)).Should().Equal(FeeErrors.AmountInvalidCode);
        Codes(Draft(100_000_001m)).Should().Equal(FeeErrors.AmountInvalidCode);
        Codes(new FeeBeneficiary.Draft(FeeNature.Fees, null!)).Should().Equal(FeeErrors.ProfessionRequiredCode);

        FeeBeneficiary beneficiary = FeeBeneficiary.Create(Supplier, Draft(200m), Now).Value;
        Result refused = beneficiary.Update(Draft(-5m, FeeNature.Copyright), Now.AddDays(1));
        refused.IsFailure.Should().BeTrue();
        beneficiary.Nature.Should().Be(FeeNature.Fees);
        beneficiary.PaidOutsideFor(Year2025).Should().Be(200m);
        beneficiary.UpdatedAt.Should().Be(Now);
        FluentActions.Invoking(() => beneficiary.Update(null!, Now)).Should().Throw<ArgumentNullException>();
        FeeErrors.YearInvalid.Code.Should().Be(FeeErrors.YearInvalidCode);
        FeeErrors.BeneficiaryNotFound.Code.Should().Be(FeeErrors.BeneficiaryNotFoundCode);
        FeeErrors.SupplierNotFound.Code.Should().Be(FeeErrors.SupplierNotFoundCode);

        static IEnumerable<string> Codes(FeeBeneficiary.Draft draft) => FeeBeneficiary.Create(Supplier, draft, Now).Errors.Select(e => e.Code);
    }
}
