using Lettra.Domain.Parties;

namespace Lettra.Domain.Tests.Parties;

public sealed class PayeeNamesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Assurance de la Vallée SA", "ASSURANCE DE LA VALLEE")]
    [InlineData("Sté Expert-comptable du Lac SAS", "Expert comptable Lac")]
    [InlineData("Dupont & Fils", "Fils et Dupont")]
    [InlineData("Boulangerie Martin SARL", "boulangerie  martin")]
    public void Les_accents_la_casse_la_ponctuation_les_formes_juridiques_et_l_ordre_ne_comptent_pas(string expected, string holder) =>
        PayeeNames.Compare(expected, holder).Should().Be(PayeeMatch.Match);

    [Theory]
    [InlineData("Minoterie Dupuis", "Minoterie Dupuy")]
    [InlineData("Papeterie Durand", "Durand")]
    [InlineData("Hébergeur Les Pins", "Hebergeur Les Pins Services")]
    public void Une_faute_de_frappe_ou_un_nom_abrege_correspond_presque(string expected, string holder) =>
        PayeeNames.Compare(expected, holder).Should().Be(PayeeMatch.CloseMatch);

    [Theory]
    [InlineData("Minoterie Dupuis", "Schmidt Handels GmbH")]
    [InlineData("Minoterie Dupuis", "")]
    [InlineData("SAS", "SARL")]
    [InlineData("Papeterie Durand", "Do")]
    public void Deux_noms_sans_rapport_ne_correspondent_pas(string expected, string holder) =>
        PayeeNames.Compare(expected, holder).Should().Be(PayeeMatch.NoMatch);

    [Fact]
    public void Les_mots_d_un_nom_sont_ceux_qui_le_distinguent()
    {
        PayeeNames.Words("Sté Assurance de la Vallée SA").Should().Equal("ASSURANCE", "VALLEE");
        PayeeNames.Words("  ").Should().BeEmpty();
        PayeeNames.Similarity(string.Empty, string.Empty).Should().Be(1d);
        PayeeNames.Similarity("ABCD", "ABCE").Should().Be(0.75);
    }

    private static Supplier NewSupplier(string? holder = null, string? iban = "FR7630006000011234567890189") =>
        Supplier.Create(
            SupplierId.New(),
            new("Minoterie Dupuis", null, "73282932000074", null, null, iban, "2 chemin du Moulin", null, "45000", "Orléans", "FR", holder),
            Now).Value;

    [Fact]
    public void Le_titulaire_du_compte_est_facultatif_et_tient_en_70_caracteres()
    {
        NewSupplier().PayeeName.Should().Be("Minoterie Dupuis");
        NewSupplier("  M. Jean Dupuis  ").PayeeName.Should().Be("M. Jean Dupuis");
        Supplier.Create(
                SupplierId.New(),
                new("Minoterie Dupuis", null, "73282932000074", null, null, null, "2 chemin du Moulin", null, "45000", "Orléans", "FR", new string('x', 71)),
                Now)
            .Errors.Should().ContainSingle().Which.Should().Be(SupplierErrors.AccountHolderTooLong);
    }

    [Fact]
    public void La_reponse_de_la_banque_est_gardee_puis_effacee_quand_l_iban_ou_le_nom_change()
    {
        Supplier supplier = NewSupplier();
        supplier.RecordPayeeCheck(PayeeMatch.CloseMatch, " Minoterie Dupuy ", Now).IsSuccess.Should().BeTrue();
        (supplier.PayeeCheckResult, supplier.PayeeCheckedAt, supplier.PayeeCheckSuggestedName).Should().Be((PayeeMatch.CloseMatch, Now, "Minoterie Dupuy"));

        supplier.Update(new("Minoterie Dupuis", null, "73282932000074", null, "compta@dupuis.fr", "FR7630006000011234567890189", "2 chemin du Moulin", null, "45000", "Orléans", "FR"), Now);
        supplier.PayeeCheckResult.Should().Be(PayeeMatch.CloseMatch, "ni l'IBAN ni le nom n'ont changé");

        supplier.Update(new("Minoterie Dupuis", null, "73282932000074", null, null, "FR7630006000011234567890189", "2 chemin du Moulin", null, "45000", "Orléans", "FR", "Jean Dupuis"), Now);
        supplier.PayeeCheckResult.Should().BeNull("le nom présenté à la banque a changé");

        supplier.RecordPayeeCheck(PayeeMatch.Match, "ignoré", Now);
        supplier.PayeeCheckSuggestedName.Should().BeNull("seul « correspond presque » propose un nom");
        supplier.Update(new("Minoterie Dupuis", null, "73282932000074", null, null, "DE89370400440532013000", "2 chemin du Moulin", null, "45000", "Orléans", "FR", "Jean Dupuis"), Now);
        supplier.PayeeCheckedAt.Should().BeNull("l'IBAN a changé");
    }

    [Fact]
    public void Sans_iban_rien_n_est_a_verifier()
    {
        Supplier supplier = NewSupplier(iban: null);

        supplier.RecordPayeeCheck(PayeeMatch.Match, null, Now).Errors.Should().ContainSingle().Which.Should().Be(SupplierErrors.IbanMissing);
        FluentActions.Invoking(() => NewSupplier().RecordPayeeCheck((PayeeMatch)9, null, Now)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Un_nom_propose_trop_long_est_coupe_a_70_caracteres()
    {
        Supplier supplier = NewSupplier();
        supplier.RecordPayeeCheck(PayeeMatch.CloseMatch, new string('y', 80), Now);
        supplier.PayeeCheckSuggestedName.Should().HaveLength(Supplier.MaxAccountHolderLength);
    }
}
