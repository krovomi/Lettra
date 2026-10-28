using Lettra.Calculations.Receipts;

namespace Lettra.Domain.Tests.Calculations;

public sealed class ReceiptTextReaderTests
{
    private static readonly DateOnly Today = new(2026, 10, 28);

    [Fact]
    public void Un_ticket_de_caisse_donne_total_date_et_TVA()
    {
        const string ticket = """
            BOULANGERIE DU MARCHÉ
            12 rue des Lilas 69003 LYON
            N° TVA FR12345678901
            Le 14/10/2026 à 08:42
            2 CROISSANTS            2,40
            1 PAIN DE CAMPAGNE      3,10
            SOUS-TOTAL              5,50
            TOTAL TTC               5,50 €
            TVA 5,5%                0,29
            CB                      5,50
            MERCI DE VOTRE VISITE
            """;

        ReceiptReading reading = ReceiptTextReader.Read(ticket, Today);

        reading.Should().Be(new ReceiptReading(5.50m, new DateOnly(2026, 10, 14), 0.29m, 5.5m));
    }

    [Fact]
    public void Une_facture_avec_un_tableau_de_TVA_par_taux_additionne_les_taux()
    {
        const string facture = """
            FACTURE N° F-2026-0412
            Date : 2026-09-30
            Désignation          Qté    PU HT    TVA
            Repas                 1    18,18     10%
            Vin                   1    12,50     20%
            Taux      Base HT      TVA       TTC
            10%        18,18      1,82     20,00
            20%        12,50      2,50     15,00
            Total HT  30,68
            Total TTC 35,00
            """;

        ReceiptReading reading = ReceiptTextReader.Read(facture, Today);

        reading.AmountInclTax.Should().Be(35.00m);
        reading.Date.Should().Be(new DateOnly(2026, 9, 30));
        reading.VatAmount.Should().Be(4.32m);
        reading.VatRate.Should().BeNull("deux taux");
        reading.VatComputed.Should().BeFalse();
    }

    [Fact]
    public void Une_ligne_total_TVA_l_emporte()
    {
        const string ticket = """
            STATION AVIA
            15 OCTOBRE 2026
            GAZOLE 45,12 L
            NET A PAYER EUR 84,90
            DONT TVA 20,00% 14,15
            """;

        ReceiptReading reading = ReceiptTextReader.Read(ticket, Today);

        reading.Should().Be(new ReceiptReading(84.90m, new DateOnly(2026, 10, 15), 14.15m, 20m));
    }

    [Fact]
    public void Sans_montant_de_TVA_mais_avec_un_seul_taux_la_TVA_est_deduite_et_signalee()
    {
        ReceiptReading reading = ReceiptTextReader.Read("PARKING CENTRE\n03.10.26\nTOTAL 12,00\nTVA 20%", Today);

        reading.AmountInclTax.Should().Be(12.00m);
        reading.Date.Should().Be(new DateOnly(2026, 10, 3));
        reading.VatAmount.Should().Be(2.00m);
        reading.VatComputed.Should().BeTrue();
    }

    [Fact]
    public void Les_confusions_de_la_reconnaissance_sont_tolerees()
    {
        ReceiptReading reading = ReceiptTextReader.Read("TOTAL 1O,5O\nTVA 2O% 1,75\nle 2/1O/2026", Today);

        reading.AmountInclTax.Should().Be(10.50m);
        reading.VatAmount.Should().Be(1.75m);
        reading.Date.Should().Be(new DateOnly(2026, 10, 2));
    }

    [Fact]
    public void Les_grands_montants_et_les_separateurs_de_milliers_sont_lus()
    {
        ReceiptReading reading = ReceiptTextReader.Read("Total TTC 1 234,56\nTotal TVA 205,76\n01/06/2026", Today);

        reading.AmountInclTax.Should().Be(1234.56m);
        reading.VatAmount.Should().Be(205.76m);
    }

    [Fact]
    public void Sans_mot_de_total_le_plus_grand_montant_est_propose()
    {
        ReceiptReading reading = ReceiptTextReader.Read("Article A 4,00\nArticle B 6,50\n", Today);

        reading.AmountInclTax.Should().Be(6.50m);
        reading.VatAmount.Should().BeNull();
        reading.Date.Should().BeNull();
    }

    [Fact]
    public void Une_date_future_ou_trop_ancienne_et_une_date_impossible_sont_ecartees()
    {
        ReceiptTextReader.Read("31/02/2026\n12/12/2030\n01/01/2015\nTOTAL 3,00", Today).Date.Should().BeNull();
        ReceiptTextReader.Read("Le 1er mars 2026", Today).Date.Should().Be(new DateOnly(2026, 3, 1));
    }

    [Fact]
    public void Une_date_n_est_pas_lue_comme_un_montant()
    {
        ReceiptTextReader.Read("12.10.2026\nTOTAL 9,90", Today).AmountInclTax.Should().Be(9.90m);
        ReceiptTextReader.Read("12.10.2026", Today).AmountInclTax.Should().BeNull();
    }

    [Fact]
    public void Un_tableau_d_articles_avec_une_colonne_TVA_ne_donne_pas_la_TVA()
    {
        const string facture = """
            Désignation  Qté  PU  TVA  Total
            Café          1  2,50  20%  2,50
            TOTAL TTC 2,50
            """;

        ReceiptReading reading = ReceiptTextReader.Read(facture, Today);

        reading.AmountInclTax.Should().Be(2.50m);
        reading.VatAmount.Should().BeNull("une ligne d'article n'est pas une ligne de TVA : l'utilisateur saisit la TVA");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Bonjour, merci de votre visite")]
    public void Un_texte_vide_ou_sans_chiffre_ne_propose_rien(string? text) =>
        ReceiptTextReader.Read(text, Today).IsEmpty.Should().BeTrue();
}
