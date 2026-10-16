using System.Diagnostics;
using System.Globalization;
using Lettra.UI.Shared.Typing;

namespace Lettra.UI.Tests.Typing;

/// <summary>Aide à la saisie sans la voix (vague 5, lane F) : saisie rapide, nettoyage, codes postaux, habitudes du dossier.</summary>
public sealed class TypingLogicTests
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    [Theory]
    [InlineData("3", 3, null, false)]
    [InlineData("3h", 3, "HUR", false)]
    [InlineData("3 heures", 3, "HUR", false)]
    [InlineData("1,5 h", 1.5, "HUR", false)]
    [InlineData("1h30", 1.5, "HUR", true)]
    [InlineData("2h15min", 2.25, "HUR", true)]
    [InlineData("45 min", 0.75, "HUR", true)]
    [InlineData("1,5 j", 1.5, "DAY", false)]
    [InlineData("2 jours", 2, "DAY", false)]
    [InlineData("2x45", 90, null, true)]
    [InlineData("2 * 3,5", 7, null, true)]
    [InlineData("3 × 2", 6, null, true)]
    [InlineData("2x3h", 6, "HUR", true)]
    public void La_saisie_rapide_des_quantites_est_comprise(string saisie, double valeur, string? unite, bool calcule)
    {
        QuickEntry.TryParseQuantity(saisie, Fr, out QuickValue value).Should().BeTrue();

        value.Should().Be(new QuickValue((decimal)valeur, unite, calcule));
    }

    [Theory]
    [InlineData("1.5d", 1.5, "DAY")]
    [InlineData("2 hours", 2, "HUR")]
    [InlineData("1,000x2", 2000, null)]
    public void En_anglais_aussi(string saisie, double valeur, string? unite)
    {
        QuickEntry.TryParseQuantity(saisie, En, out QuickValue value).Should().BeTrue();
        value.Value.Should().Be((decimal)valeur);
        value.UnitCode.Should().Be(unite);
    }

    [Theory]
    [InlineData("")]
    [InlineData("trois")]
    [InlineData("2x")]
    [InlineData("1h75")]
    [InlineData("3 kg")]
    public void Une_saisie_inconnue_est_refusee(string saisie) =>
        QuickEntry.TryParseQuantity(saisie, Fr, out _).Should().BeFalse();

    [Fact]
    public void Les_montants_acceptent_le_produit_et_le_symbole_euro_mais_pas_d_unite()
    {
        QuickEntry.TryParseAmount("2x45", Fr, out QuickValue produit).Should().BeTrue();
        produit.Should().Be(new QuickValue(90m, null, true));
        QuickEntry.TryParseAmount("1 234,50 €", Fr, out QuickValue montant).Should().BeTrue();
        montant.Value.Should().Be(1234.5m);
        QuickEntry.TryParseAmount("3h", Fr, out _).Should().BeFalse();
        QuickEntry.IsQuickForm("120").Should().BeFalse();
        QuickEntry.IsQuickForm("2x45").Should().BeTrue();
        QuickEntry.IsQuickForm("3 h").Should().BeTrue();
    }

    [Theory]
    [InlineData("  tenue   comptable  ", "Tenue comptable")]
    [InlineData("conseil , suivi .", "Conseil, suivi.")]
    [InlineData("première ligne  \n  seconde   ligne ", "Première ligne\nseconde ligne")]
    [InlineData("m3 de béton", "m3 de béton")]
    [InlineData("iPhone fourni", "iPhone fourni")]
    [InlineData("« bon pour accord »", "« Bon pour accord »")]
    [InlineData("   ", "")]
    public void Le_texte_libre_est_nettoye_a_la_sortie_du_champ(string saisie, string attendu) =>
        TextTidy.Apply(saisie, Fr).Should().Be(attendu);

    [Theory]
    [InlineData("/", 0, "")]
    [InlineData("Merci /rem", 6, "rem")]
    [InlineData("Paiement à 30 j/fin", null, null)]
    [InlineData("rien", null, null)]
    public void Le_dernier_mot_en_barre_oblique_appelle_un_modele(string texte, int? debut, string? recherche)
    {
        (int Start, string Query)? token = SmartInput.TemplateToken(texte);
        if (debut is null)
        {
            token.Should().BeNull();
            return;
        }

        token.Should().Be((debut.Value, recherche!));
    }

    [Fact]
    public void Les_modeles_sont_ranges_par_champ()
    {
        TextTemplates.For(TextFieldKind.PaymentTerms).Select(t => t.Id).Should().Contain(["terms-30", "late-penalties"]).And.NotContain("quote-validity");
        TextTemplates.For(TextFieldKind.QuoteNotes).Select(t => t.Id).Should().Contain(["quote-validity", "vat-franchise"]);
        TextTemplates.For(TextFieldKind.Free).Should().BeEmpty();
        TextTemplates.All.Select(t => t.Id).Should().OnlyHaveUniqueItems();
    }

    // ---------- codes postaux ----------

    private static readonly Lazy<PostalCodeDirectory> Communes = new(() =>
        PostalCodeDirectory.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Typing", "codes-postaux.txt"))));

    [Fact]
    public void Le_fichier_des_communes_couvre_la_France()
    {
        Communes.Value.Count.Should().BeGreaterThan(35_000);
        Communes.Value.ByPostalCode("75009").Should().ContainSingle().Which.Should().Be(new PostalPlace("75009", "Paris 9e Arrondissement"));
        Communes.Value.ByPostalCode("97400").Should().Contain(p => p.City == "Saint-Denis", "outre-mer compris");
    }

    [Fact]
    public void Le_code_postal_propose_ses_communes_et_la_ville_ses_codes()
    {
        Communes.Value.ByPostalCode("750").Should().HaveCount(PostalCodeDirectory.MaxResults).And.OnlyContain(p => p.PostalCode.StartsWith("750", StringComparison.Ordinal));
        Communes.Value.ByPostalCode("7").Should().BeEmpty("deux chiffres au moins");
        Communes.Value.ByPostalCode("ab").Should().BeEmpty();

        Communes.Value.ByCity("st etienne").Should().BeEmpty("« st » n'est pas « saint »");
        Communes.Value.ByCity("saint etienne").Should().Contain(p => p.City == "Saint-Étienne" && p.PostalCode == "42000");
        Communes.Value.ByCity("lyo").Should().Contain(p => p.City == "Lyon 1er Arrondissement");
        Communes.Value.ByCity("denis").Should().Contain(p => p.City == "Saint-Denis", "un mot du nom suffit");
        Communes.Value.ByCity("paris")[0].City.Should().StartWith("Paris", "les noms qui commencent par le texte passent d'abord");
    }

    [Fact]
    public void Chercher_une_commune_prend_moins_de_100_ms()
    {
        PostalCodeDirectory communes = Communes.Value;
        communes.ByCity("mont");
        Stopwatch chrono = Stopwatch.StartNew();
        for (int i = 0; i < 20; i++)
        {
            communes.ByCity("saint");
            communes.ByPostalCode("69");
        }

        (chrono.Elapsed.TotalMilliseconds / 20).Should().BeLessThan(100);
    }

    // ---------- habitudes du dossier ----------

    private static readonly Guid Dupont = Guid.NewGuid(), Martin = Guid.NewGuid();
    private static readonly Guid Tenue = Guid.NewGuid(), Bilan = Guid.NewGuid(), Paie = Guid.NewGuid();

    private static HistoryDocument Piece(Guid client, int jour, string? conditions, params (Guid? Article, string Designation)[] lignes) =>
        new(client, new DateOnly(2026, 10, jour), conditions, [.. lignes.Select(l => new HistoryLine(l.Article, null, l.Designation))]);

    [Fact]
    public void Les_articles_habituels_du_client_viennent_du_plus_frequent()
    {
        DossierHistory historique = new([
            Piece(Dupont, 1, "30 jours", (Tenue, "Tenue mensuelle"), (Bilan, "Bilan")),
            Piece(Dupont, 5, null, (Tenue, "Tenue mensuelle d'octobre"), (Tenue, "Tenue (rattrapage)")),
            Piece(Dupont, 3, "À réception", (Paie, "Paie"), (null, "Frais de port")),
            Piece(Martin, 9, "45 jours fin de mois", (Bilan, "Bilan Martin")),
        ]);

        historique.ItemsFor(Dupont).Select(h => (h.CatalogItemId, h.Uses)).Should().Equal((Tenue, 2), (Paie, 1), (Bilan, 1));
        historique.ItemsFor(Dupont)[0].Designation.Should().Be("Tenue mensuelle d'octobre", "la plus récente");
        historique.LastPaymentTermsFor(Dupont).Should().Be("À réception");
        historique.LastPaymentTermsFor(Guid.NewGuid()).Should().BeNull();
        historique.DesignationsFor(Bilan).Should().Equal("Bilan Martin", "Bilan");
        historique.DesignationsFor(null).Should().HaveCount(7).And.Contain("Frais de port");
    }

    [Fact]
    public void Une_suggestion_arrive_en_moins_de_100_ms_sur_un_gros_dossier()
    {
        Random hasard = new(42);
        Guid[] clients = [.. Enumerable.Range(0, 200).Select(_ => Guid.NewGuid())];
        Guid[] articles = [.. Enumerable.Range(0, 300).Select(_ => Guid.NewGuid())];
        DossierHistory historique = new(Enumerable.Range(0, 5_000).Select(i => Piece(
            clients[hasard.Next(clients.Length)], 1 + (i % 28), i % 3 == 0 ? "30 jours" : null,
            [.. Enumerable.Range(0, 6).Select(l => ((Guid?)articles[hasard.Next(articles.Length)], $"Prestation {l}"))])));

        // Un premier appel compile le code (JIT) : il n'est pas compté, l'utilisateur ne le paie qu'une fois. On garde
        // la médiane de cinq suggestions sur des clients différents pour ne pas dépendre d'un à-coup de la machine.
        Suggerer(historique, clients[^1], articles[^1]);
        double[] durees = [.. Enumerable.Range(0, 5).Select(i =>
        {
            Stopwatch chrono = Stopwatch.StartNew();
            Suggerer(historique, clients[i], articles[i]);
            return chrono.Elapsed.TotalMilliseconds;
        }).Order()];
        durees[2].Should().BeLessThan(100);
    }

    private static void Suggerer(DossierHistory historique, Guid client, Guid article)
    {
        historique.ItemsFor(client);
        historique.LastPaymentTermsFor(client);
        historique.DesignationsFor(article);
    }
}
