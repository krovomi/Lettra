namespace Lettra.Speech.Tests;

public sealed class DictationFormatterTests
{
    private const char Nnbsp = ' ';
    private const char Nbsp = ' ';

    private static string Fr(string text) => DictationFormatter.Format(text, SpokenLanguage.French);

    private static string En(string text) => DictationFormatter.Format(text, SpokenLanguage.English);

    [Fact]
    public void La_ponctuation_dictee_devient_des_signes() =>
        Fr("bonjour virgule merci pour votre retour point").Should().Be("Bonjour, merci pour votre retour.");

    [Fact]
    public void La_ponctuation_ajoutee_par_le_moteur_autour_d_un_signe_dicte_n_est_pas_doublee() =>
        Fr("Bonjour, virgule, merci pour votre retour. Point.").Should().Be("Bonjour, merci pour votre retour.");

    [Fact]
    public void Les_signes_doubles_ont_l_espace_fine_insecable_francaise() =>
        Fr("vous venez demain point d'interrogation").Should().Be($"Vous venez demain{Nnbsp}?");

    [Fact]
    public void Les_deux_points_ont_l_espace_insecable() =>
        Fr("objet deux points devis de rénovation").Should().Be($"Objet{Nbsp}: devis de rénovation");

    [Fact]
    public void A_la_ligne_et_nouveau_paragraphe_coupent_le_texte_et_relancent_la_majuscule() =>
        Fr("Madame, à la ligne. veuillez trouver notre devis. Nouveau paragraphe. cordialement")
            .Should().Be("Madame\nVeuillez trouver notre devis.\n\nCordialement");

    [Fact]
    public void Les_guillemets_et_parentheses_dictes_sont_places() =>
        Fr("le chantier ouvrez les guillemets Dupont fermez les guillemets ouvrez la parenthèse lot deux fermez la parenthèse")
            .Should().Be($"Le chantier «{Nbsp}Dupont{Nbsp}» (lot deux)");

    [Theory]
    [InlineData("cent vingt euros", "120,00 €")]
    [InlineData("à 120 euros HT", "À 120,00 € HT")]
    [InlineData("1 200 €", "1 200,00 €")]
    [InlineData("douze euros cinquante", "12,50 €")]
    [InlineData("douze euros et cinquante centimes", "12,50 €")]
    [InlineData("mille deux cent cinquante euros", "1 250,00 €")]
    [InlineData("dix pour cent", "10 %")]
    [InlineData("12,5 %", "12,5 %")]
    public void Les_montants_et_pourcentages_sont_ecrits_en_chiffres(string dictated, string expected) =>
        Fr(dictated).Should().Be(expected);

    [Fact]
    public void Les_nombres_sans_unite_restent_tels_quels() =>
        Fr("un client a commandé trois fenêtres").Should().Be("Un client a commandé trois fenêtres");

    [Theory]
    [InlineData("à ce point")]
    [InlineData("le point de vente")]
    [InlineData("faire le point sur le chantier")]
    [InlineData("mise au point")]
    public void Point_reste_un_nom_quand_c_en_est_un(string text) =>
        Fr(text).Should().Be(char.ToUpperInvariant(text[0]) + text[1..]);

    [Fact]
    public void Les_mots_composes_et_les_elisions_gardent_leur_graphie() =>
        Fr("pose d'une porte-fenêtre aujourd'hui").Should().Be("Pose d'une porte-fenêtre aujourd'hui");

    [Fact]
    public void Une_dictee_vide_donne_un_texte_vide()
    {
        Fr("").Should().BeEmpty();
        Fr("   ").Should().BeEmpty();
        Fr("virgule").Should().BeEmpty();
    }

    [Fact]
    public void En_anglais_la_ponctuation_et_les_montants_suivent_l_usage_anglais() =>
        En("thanks comma the total is one hundred twenty euros period new line see you")
            .Should().Be("Thanks, the total is €120.00.\nSee you");

    [Fact]
    public void En_anglais_period_reste_un_nom_quand_c_en_est_un() =>
        En("the trial period ends today period").Should().Be("The trial period ends today.");

    [Fact]
    public void Les_points_de_suspension_du_moteur_restent_un_seul_signe() =>
        Fr("je vous rappelle...").Should().Be("Je vous rappelle…");
}
