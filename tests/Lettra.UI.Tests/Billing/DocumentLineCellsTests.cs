using System.Globalization;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.UI.Shared.Billing;

namespace Lettra.UI.Tests.Billing;

public sealed class DocumentLineCellsTests
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private static readonly BillingReferenceData Reference = new(
        [],
        [new VatExemptionReasonOption("VATEX-EU-132", "Exonération, article 261 du CGI")],
        [new VatRateOption("FR-20", 20m), new VatRateOption("FR-5.5", 5.5m)]);

    private static DocumentLineRow Row() => DocumentLineRow.New(new VatRateOption("FR-20", 20m));

    [Fact]
    public void La_quantite_rapide_pose_aussi_l_unite_de_temps()
    {
        DocumentLineRow line = DocumentLineCells.ParseQuantity(Row(), "3h", French);

        line.Quantity.Should().Be(3m);
        line.UnitCode.Should().Be("HUR");
        FluentActions.Invoking(() => DocumentLineCells.ParseQuantity(Row(), "trois", French)).Should().Throw<FormatException>();
    }

    [Fact]
    public void Le_prix_accepte_un_produit_mais_pas_un_montant_negatif()
    {
        DocumentLineCells.ParsePrice(Row(), "2x45", French).UnitPriceExclTax.Should().Be(90m);
        FluentActions.Invoking(() => DocumentLineCells.ParsePrice(Row(), "-5", French)).Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("10 %", 10)]
    [InlineData("12,5", 12.5)]
    public void La_remise_se_lit_en_pourcentage(string text, decimal expected) =>
        DocumentLineCells.ParseDiscount(Row(), text, French).DiscountPercent.Should().Be(expected);

    [Fact]
    public void Une_remise_au_dela_de_100_est_refusee() =>
        FluentActions.Invoking(() => DocumentLineCells.ParseDiscount(Row(), "120", French)).Should().Throw<FormatException>();

    [Fact]
    public void La_tva_se_lit_par_code_par_taux_ou_par_motif_d_exoneration()
    {
        DocumentLineCells.ParseVat(Row(), "fr-5.5", Reference, VatRegimeDto.Standard, French).VatRatePercent.Should().Be(5.5m);
        DocumentLineCells.ParseVat(Row(), "5,5 %", Reference, VatRegimeDto.Standard, French).VatRateCode.Should().Be("FR-5.5");
        DocumentLineCells.ParseVat(Row(), "VATEX-EU-132", Reference, VatRegimeDto.Standard, French).VatExemptionCode.Should().Be("VATEX-EU-132");
    }

    [Fact]
    public void Un_motif_d_exoneration_hors_regime_normal_ou_un_taux_inconnu_est_refuse()
    {
        VatRegimeDto other = Enum.GetValues<VatRegimeDto>().First(r => r != VatRegimeDto.Standard);

        FluentActions.Invoking(() => DocumentLineCells.ParseVat(Row(), "VATEX-EU-132", Reference, other, French)).Should().Throw<FormatException>();
        FluentActions.Invoking(() => DocumentLineCells.ParseVat(Row(), "7", Reference, VatRegimeDto.Standard, French)).Should().Throw<FormatException>();
    }
}
