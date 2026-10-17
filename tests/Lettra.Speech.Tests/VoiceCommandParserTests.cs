using Lettra.Contracts.Speech;

namespace Lettra.Speech.Tests;

public sealed class VoiceCommandParserTests
{
    private static VoiceCommand? Fr(string text) => VoiceCommandParser.Parse(text, SpokenLanguage.French);

    private static VoiceCommand? En(string text) => VoiceCommandParser.Parse(text, SpokenLanguage.English);

    [Theory]
    [InlineData("Nouveau devis.", VoiceTarget.Quotes)]
    [InlineData("nouvelle facture", VoiceTarget.Invoices)]
    [InlineData("Créer un client", VoiceTarget.Customers)]
    [InlineData("nouvel article", VoiceTarget.Catalog)]
    [InlineData("nouvel avoir", VoiceTarget.CreditNotes)]
    [InlineData("Lettra, nouveau fournisseur s'il te plaît", VoiceTarget.Suppliers)]
    public void Nouveau_cree_l_entite_nommee(string phrase, VoiceTarget target)
    {
        VoiceCommand? command = Fr(phrase);
        command.Should().NotBeNull();
        command!.Kind.Should().Be(VoiceCommandKind.Create);
        command.Target.Should().Be(target);
        command.Phrase.Should().Be(phrase.Trim());
    }

    [Theory]
    [InlineData("Ouvrir les factures en retard", VoiceTarget.Invoices, VoiceFilter.Overdue)]
    [InlineData("affiche les devis acceptés", VoiceTarget.Quotes, VoiceFilter.Accepted)]
    [InlineData("Montre-moi les factures impayées", VoiceTarget.Invoices, VoiceFilter.Unpaid)]
    [InlineData("les devis en brouillon", VoiceTarget.Quotes, VoiceFilter.Draft)]
    [InlineData("va aux factures qui sont en retard", VoiceTarget.Invoices, VoiceFilter.Overdue)]
    public void Ouvrir_une_liste_avec_son_filtre(string phrase, VoiceTarget target, VoiceFilter filter)
    {
        VoiceCommand? command = Fr(phrase);
        command.Should().BeEquivalentTo(new { Kind = VoiceCommandKind.Navigate, Target = target, Filter = filter, Term = (string?)null });
    }

    [Theory]
    [InlineData("ouvre le catalogue", VoiceTarget.Catalog)]
    [InlineData("aller au tableau de bord", VoiceTarget.Home)]
    [InlineData("Afficher le centre d'alertes", VoiceTarget.Alerts)]
    [InlineData("ouvrir le FEC", VoiceTarget.Fec)]
    [InlineData("synchronisation", VoiceTarget.Sync)]
    [InlineData("Factures.", VoiceTarget.Invoices)]
    public void Ouvrir_un_ecran(string phrase, VoiceTarget target) =>
        Fr(phrase).Should().BeEquivalentTo(new { Kind = VoiceCommandKind.Navigate, Target = target, Filter = (VoiceFilter?)null });

    [Fact]
    public void Ouvrir_une_liste_d_un_client_garde_son_nom_comme_filtre() =>
        Fr("ouvre les factures de Dupont").Should().BeEquivalentTo(new { Kind = VoiceCommandKind.Navigate, Target = VoiceTarget.Invoices, Term = "Dupont" });

    [Theory]
    [InlineData("Chercher le client Dupont", VoiceTarget.Customers, "Dupont")]
    [InlineData("trouve la facture 2026-0042", VoiceTarget.Invoices, "2026-0042")]
    [InlineData("recherche l'article pose de carrelage.", VoiceTarget.Catalog, "pose de carrelage")]
    public void Chercher_dans_une_liste(string phrase, VoiceTarget target, string term) =>
        Fr(phrase).Should().BeEquivalentTo(new { Kind = VoiceCommandKind.Search, Target = target, Term = term });

    [Fact]
    public void Chercher_sans_entite_cherche_partout() =>
        Fr("rechercher Martin et Fils").Should().BeEquivalentTo(new { Kind = VoiceCommandKind.Search, Target = (VoiceTarget?)null, Term = "Martin et Fils" });

    [Fact]
    public void Ajouter_une_ligne_lit_quantite_unite_designation_et_prix()
    {
        VoiceCommand? command = Fr("Ajouter trois heures de conseil à cent vingt euros HT.");
        command!.Kind.Should().Be(VoiceCommandKind.AddLine);
        command.Lines.Should().ContainSingle().Which.Should().Be(new VoiceLine(3, "HUR", "conseil", 120m, false));
    }

    [Theory]
    [InlineData("ajoute une ligne de déplacement", 1, null, "déplacement", null, null)]
    [InlineData("ajouter une heure et demie d'audit à 90 € de l'heure", 1.5, "HUR", "audit", 90.0, null)]
    [InlineData("ajoute une demi-journée de formation à 450 euros TTC", 0.5, "DAY", "formation", 450.0, true)]
    [InlineData("ajouter 2 forfaits déplacement à 45 euros", 2, "LS", "déplacement", 45.0, null)]
    [InlineData("ajoute 12 mètres carrés de carrelage à 35,50 € HT", 12, "MTK", "carrelage", 35.5, false)]
    [InlineData("ajouter pose de porte-fenêtre pour 320 euros", 1, null, "pose de porte-fenêtre", 320.0, null)]
    [InlineData("ajoute cinq licences 49 euros", 5, null, "licences", 49.0, null)]
    [InlineData("ajouter trois jours de développement à 600", 3, "DAY", "développement", 600.0, null)]
    [InlineData("ajoute deux heures d'audit à 90 euros", 2, "HUR", "audit", 90.0, null)] // exemple du README
    public void Les_lignes_dites_de_plusieurs_facons_sont_lues(string phrase, double quantity, string? unit, string designation, double? price, bool? includesVat) =>
        Fr(phrase)!.Lines.Should().ContainSingle().Which.Should().Be(
            new VoiceLine((decimal)quantity, unit, designation, price is null ? null : (decimal)price, includesVat));

    [Theory]
    [InlineData("remise de 10 %", 10)]
    [InlineData("appliquer une remise de dix pour cent", 10)]
    [InlineData("réduction de 12,5 pourcent", 12.5)]
    [InlineData("15 % de remise", 15)]
    [InlineData("remise de dix pour cent", 10)] // exemple du README
    public void Remise(string phrase, double percent) =>
        Fr(phrase).Should().BeEquivalentTo(new { Kind = VoiceCommandKind.ApplyDiscount, DiscountPercent = (decimal)percent });

    [Fact]
    public void Un_devis_complet_dicte_donne_un_brouillon_a_relire()
    {
        VoiceCommand? command = Fr("Devis pour Dupont, trois heures de conseil à 120 € HT, remise 10 %");
        command!.Kind.Should().Be(VoiceCommandKind.DraftQuote);
        command.Target.Should().Be(VoiceTarget.Quotes);
        command.Customer.Should().Be("Dupont");
        command.Lines.Should().Equal(new VoiceLine(3, "HUR", "conseil", 120m, false));
        command.DiscountPercent.Should().Be(10);
        command.RequiresConfirmation.Should().BeFalse();
    }

    [Fact]
    public void Un_devis_dicte_sans_virgules_retrouve_le_client_et_les_lignes()
    {
        VoiceCommand? command = Fr("nouveau devis pour la société Martin et Fils deux jours de formation à 600 euros et trois heures de conseil à 120 euros");
        command!.Kind.Should().Be(VoiceCommandKind.DraftQuote);
        command.Customer.Should().Be("Martin et Fils");
        command.Lines.Should().Equal(
            new VoiceLine(2, "DAY", "formation", 600m, null),
            new VoiceLine(3, "HUR", "conseil", 120m, null));
    }

    [Fact]
    public void Une_partie_illisible_du_devis_reste_une_ligne_a_relire()
    {
        VoiceCommand? command = Fr("devis pour Durand, trois heures de conseil à 120 euros, le reste comme d'habitude");
        command!.Lines.Should().Equal(
            new VoiceLine(3, "HUR", "conseil", 120m, null),
            new VoiceLine(1, null, "le reste comme d'habitude", null, null));
    }

    [Fact]
    public void Nouveau_devis_pour_un_client_sans_ligne_ouvre_la_creation_pour_ce_client() =>
        Fr("nouveau devis pour Dupont").Should().BeEquivalentTo(new { Kind = VoiceCommandKind.Create, Target = VoiceTarget.Quotes, Customer = "Dupont" });

    [Fact]
    public void Valider_demande_toujours_une_confirmation()
    {
        Fr("Valider.").Should().BeEquivalentTo(new { Kind = VoiceCommandKind.Validate, RequiresConfirmation = true });
        Fr("valider la facture").Should().BeEquivalentTo(new { Kind = VoiceCommandKind.Validate, Target = VoiceTarget.Invoices, RequiresConfirmation = true });
    }

    [Theory]
    [InlineData("enregistrer", VoiceCommandKind.Save)]
    [InlineData("Annuler.", VoiceCommandKind.Cancel)]
    [InlineData("aide", VoiceCommandKind.Help)]
    [InlineData("qu'est-ce que je peux dire ?", VoiceCommandKind.Help)]
    public void Commandes_simples(string phrase, VoiceCommandKind kind) =>
        Fr(phrase).Should().BeEquivalentTo(new { Kind = kind, RequiresConfirmation = false });

    [Theory]
    [InlineData("")]
    [InlineData("bonjour comment allez-vous")]
    [InlineData("valider tout et envoyer au client")]
    [InlineData("il fait beau aujourd'hui")]
    [InlineData("merci")]
    public void Une_phrase_hors_grammaire_ne_donne_aucune_commande(string phrase) =>
        Fr(phrase).Should().BeNull();

    [Theory]
    [InlineData("ouvre les paramètre", VoiceTarget.Settings)]
    [InlineData("affiche les fournisseurss", VoiceTarget.Suppliers)]
    [InlineData("nouveau devi", VoiceTarget.Quotes)]
    public void Une_lettre_de_trop_ou_de_moins_ne_fait_pas_rater_la_commande(string said, VoiceTarget target) =>
        Fr(said)!.Target.Should().Be(target);

    [Theory]
    [InlineData("ouvre le cata")]
    [InlineData("ouvrir les fact")]
    public void Un_mot_court_ou_trop_different_n_est_pas_devine(string said) =>
        Fr(said).Should().BeNull();

    [Theory]
    [InlineData("open overdue invoices", VoiceTarget.Invoices, VoiceFilter.Overdue)]
    [InlineData("show draft quotes", VoiceTarget.Quotes, VoiceFilter.Draft)]
    [InlineData("unpaid invoices", VoiceTarget.Invoices, VoiceFilter.Unpaid)]
    public void En_anglais_le_filtre_peut_preceder_la_liste(string said, VoiceTarget target, VoiceFilter filter) =>
        En(said).Should().BeEquivalentTo(new { Kind = VoiceCommandKind.Navigate, Target = target, Filter = filter });

    [Fact]
    public void Les_commandes_anglaises_sont_comprises()
    {
        En("New quote").Should().BeEquivalentTo(new { Kind = VoiceCommandKind.Create, Target = VoiceTarget.Quotes });
        En("show me the overdue invoices").Should().BeEquivalentTo(new { Kind = VoiceCommandKind.Navigate, Target = VoiceTarget.Invoices, Filter = VoiceFilter.Overdue });
        En("open invoices that are overdue").Should().BeEquivalentTo(new { Kind = VoiceCommandKind.Navigate, Target = VoiceTarget.Invoices, Filter = VoiceFilter.Overdue });
        En("find customer Smith").Should().BeEquivalentTo(new { Kind = VoiceCommandKind.Search, Target = VoiceTarget.Customers, Term = "Smith" });
        En("add three hours of consulting at one hundred twenty euros excluding VAT")!.Lines
            .Should().Equal(new VoiceLine(3, "HUR", "consulting", 120m, false));
        En("apply a discount of ten percent").Should().BeEquivalentTo(new { Kind = VoiceCommandKind.ApplyDiscount, DiscountPercent = 10m });
        En("validate").Should().BeEquivalentTo(new { Kind = VoiceCommandKind.Validate, RequiresConfirmation = true });
    }
}
