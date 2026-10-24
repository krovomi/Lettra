using Lettra.Calculations;
using Lettra.Calculations.Compliance;

namespace Lettra.Domain.Tests.Calculations;

/// <summary>Une seule liste des États membres, lue par la conformité des pièces.</summary>
public sealed class EuropeanUnionTests
{
    [Fact]
    public void L_union_compte_27_etats_distincts_dont_la_France() =>
        EuropeanUnion.MemberStates.Should().HaveCount(27).And.OnlyHaveUniqueItems().And.Contain("FR");

    [Fact]
    public void VIES_interroge_les_27_etats_la_grece_en_EL_et_l_irlande_du_nord() =>
        EuropeanUnion.ViesPrefixes.Should().HaveCount(28).And.Contain(["EL", "XI", "FR"]).And.NotContain("GR");

    [Fact]
    public void La_grece_s_ecrit_GR_comme_dans_ISO_3166() =>
        EuropeanUnion.MemberStates.Should().Contain("GR").And.NotContain("EL");

    [Fact]
    public void Le_royaume_uni_n_est_plus_membre() =>
        EuropeanUnion.MemberStates.Should().NotContain(["GB", "UK", "XI"]);

    [Theory]
    [InlineData("de")]
    [InlineData("DE")]
    public void La_conformite_reconnait_un_etat_membre_quelle_que_soit_la_casse(string code) =>
        ComplianceChecker.EuCountries.Should().Contain(code);
}
