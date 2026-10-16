using Lettra.Contracts.Speech;

namespace Lettra.Speech;

/// <summary>Une expression de un ou plusieurs mots (clés sans accents) et ce qu'elle désigne.</summary>
internal sealed record Phrase<T>(string[] Keys, T Value);

/// <summary>
/// Vocabulaire des commandes vocales, en clés sans accents (« impayées » s'écrit <c>impayees</c>). Il est fermé et
/// relu : ajouter une tournure ici est la seule façon d'élargir ce que la voix comprend.
/// </summary>
internal sealed class VoiceVocabulary
{
    public static VoiceVocabulary French { get; } = new()
    {
        Fillers =
        [
            ["lettra"], ["s'", "il", "te", "plait"], ["s'", "il", "vous", "plait"], ["est", "ce", "que", "tu", "peux"],
            ["est", "ce", "que", "vous", "pouvez"], ["peux", "tu"], ["pouvez", "vous"], ["tu", "peux"], ["vous", "pouvez"],
            ["je", "veux"], ["je", "voudrais"], ["j'", "aimerais"], ["merci"], ["ok"], ["alors"], ["euh"], ["bon"],
        ],
        NavigateVerbs =
        [
            ["ouvrir"], ["ouvre"], ["ouvrez"], ["afficher"], ["affiche"], ["affichez"], ["montrer"], ["montre"], ["montrez"],
            ["voir"], ["aller"], ["va"], ["allez"], ["consulter"], ["consulte"], ["lister"], ["liste"], ["retour"], ["retourner"],
        ],
        NavigateLinks = ["moi", "nous", "a", "au", "aux", "sur", "la", "le", "les", "l'", "mes", "mon", "ma", "des", "du", "de", "d'", "dans", "vers", "toutes", "tous"],
        CreateVerbs = [["nouveau"], ["nouvelle"], ["nouvel"], ["creer"], ["cree"], ["creez"], ["faire"], ["fais"], ["faites"], ["preparer"], ["prepare"]],
        Articles = ["un", "une", "le", "la", "l'", "les", "des", "nouveau", "nouvelle", "nouvel"],
        SearchVerbs = [["chercher"], ["cherche"], ["cherchez"], ["rechercher"], ["recherche"], ["recherchez"], ["trouver"], ["trouve"], ["trouvez"]],
        AddVerbs = [["ajouter"], ["ajoute"], ["ajoutez"], ["rajouter"], ["rajoute"], ["mettre"], ["mets"], ["mettez"]],
        LineWords = [["une", "ligne"], ["la", "ligne"], ["ligne"]],
        DiscountWords = [["remise"], ["reduction"], ["rabais"], ["ristourne"]],
        DiscountVerbs = [["appliquer"], ["applique"], ["appliquez"], ["faire"], ["fais"], ["faites"], ["mettre"], ["mets"], ["ajouter"], ["ajoute"]],
        For = ["pour"],
        Of = ["de", "d'", "du", "des"],
        PricePrepositions = [["au", "prix", "de"], ["au", "tarif", "de"], ["a"], ["au"], ["pour"]],
        PerUnit =
        [
            ["de", "l'", "heure"], ["l'", "heure"], ["par", "heure"], ["le", "jour"], ["par", "jour"], ["la", "journee"],
            ["par", "journee"], ["l'", "unite"], ["par", "unite"], ["la", "piece"], ["piece"], ["chacun"], ["chacune"],
            ["le", "metre"], ["le", "mois"], ["par", "mois"],
        ],
        LineSeparators = ["puis", "plus", "et"],
        Commands =
        [
            new(["valider"], VoiceCommandKind.Validate), new(["valide"], VoiceCommandKind.Validate), new(["validez"], VoiceCommandKind.Validate),
            new(["enregistrer"], VoiceCommandKind.Save), new(["enregistre"], VoiceCommandKind.Save), new(["enregistrez"], VoiceCommandKind.Save),
            new(["sauvegarder"], VoiceCommandKind.Save), new(["sauvegarde"], VoiceCommandKind.Save),
            new(["annuler"], VoiceCommandKind.Cancel), new(["annule"], VoiceCommandKind.Cancel), new(["annulez"], VoiceCommandKind.Cancel),
            new(["fermer"], VoiceCommandKind.Cancel), new(["ferme"], VoiceCommandKind.Cancel),
            new(["aide"], VoiceCommandKind.Help), new(["aidez", "moi"], VoiceCommandKind.Help), new(["aide", "moi"], VoiceCommandKind.Help),
            new(["qu'", "est", "ce", "que", "je", "peux", "dire"], VoiceCommandKind.Help),
        ],
        Targets =
        [
            new(["tableau", "de", "bord"], VoiceTarget.Home), new(["accueil"], VoiceTarget.Home),
            new(["devis"], VoiceTarget.Quotes),
            new(["factures"], VoiceTarget.Invoices), new(["facture"], VoiceTarget.Invoices),
            new(["avoirs"], VoiceTarget.CreditNotes), new(["avoir"], VoiceTarget.CreditNotes),
            new(["clients"], VoiceTarget.Customers), new(["client"], VoiceTarget.Customers), new(["cliente"], VoiceTarget.Customers),
            new(["fournisseurs"], VoiceTarget.Suppliers), new(["fournisseur"], VoiceTarget.Suppliers),
            new(["catalogue"], VoiceTarget.Catalog), new(["articles"], VoiceTarget.Catalog), new(["article"], VoiceTarget.Catalog),
            new(["prestations"], VoiceTarget.Catalog), new(["prestation"], VoiceTarget.Catalog),
            new(["produits"], VoiceTarget.Catalog), new(["produit"], VoiceTarget.Catalog),
            new(["fichier", "des", "ecritures", "comptables"], VoiceTarget.Fec), new(["export", "fec"], VoiceTarget.Fec), new(["fec"], VoiceTarget.Fec),
            new(["centre", "d'", "alertes"], VoiceTarget.Alerts), new(["alertes"], VoiceTarget.Alerts), new(["alerte"], VoiceTarget.Alerts),
            new(["synchronisation"], VoiceTarget.Sync), new(["synchro"], VoiceTarget.Sync),
            new(["parametres"], VoiceTarget.Settings), new(["reglages"], VoiceTarget.Settings), new(["preferences"], VoiceTarget.Settings),
            new(["assistant"], VoiceTarget.Assistant),
        ],
        Filters =
        [
            new(["en", "retard"], VoiceFilter.Overdue), new(["echues"], VoiceFilter.Overdue), new(["echus"], VoiceFilter.Overdue),
            new(["non", "payees"], VoiceFilter.Unpaid), new(["non", "reglees"], VoiceFilter.Unpaid), new(["a", "encaisser"], VoiceFilter.Unpaid),
            new(["impayees"], VoiceFilter.Unpaid), new(["impayes"], VoiceFilter.Unpaid), new(["impayee"], VoiceFilter.Unpaid),
            new(["payees"], VoiceFilter.Paid), new(["reglees"], VoiceFilter.Paid), new(["encaissees"], VoiceFilter.Paid),
            new(["en", "brouillon"], VoiceFilter.Draft), new(["brouillons"], VoiceFilter.Draft), new(["brouillon"], VoiceFilter.Draft),
            new(["envoyes"], VoiceFilter.Sent), new(["envoyees"], VoiceFilter.Sent), new(["envoye"], VoiceFilter.Sent), new(["envoyee"], VoiceFilter.Sent),
            new(["acceptes"], VoiceFilter.Accepted), new(["acceptees"], VoiceFilter.Accepted), new(["signes"], VoiceFilter.Accepted),
            new(["refuses"], VoiceFilter.Refused), new(["refusees"], VoiceFilter.Refused),
            new(["expires"], VoiceFilter.Expired), new(["expirees"], VoiceFilter.Expired),
        ],
        Units =
        [
            new(["heures"], "HUR"), new(["heure"], "HUR"), new(["h"], "HUR"),
            new(["journees"], "DAY"), new(["journee"], "DAY"), new(["jours"], "DAY"), new(["jour"], "DAY"), new(["j"], "DAY"),
            new(["unites"], "C62"), new(["unite"], "C62"), new(["pieces"], "C62"), new(["piece"], "C62"),
            new(["forfaits"], "LS"), new(["forfait"], "LS"),
            new(["metres", "carres"], "MTK"), new(["metre", "carre"], "MTK"),
            new(["metres", "cubes"], "MTQ"), new(["metre", "cube"], "MTQ"),
            new(["kilometres"], "KMT"), new(["kilometre"], "KMT"), new(["km"], "KMT"),
            new(["metres"], "MTR"), new(["metre"], "MTR"),
            new(["kilogrammes"], "KGM"), new(["kilogramme"], "KGM"), new(["kilos"], "KGM"), new(["kilo"], "KGM"), new(["kg"], "KGM"),
            new(["litres"], "LTR"), new(["litre"], "LTR"),
            new(["mois"], "MON"),
        ],
        CustomerPrefixes = [["le", "client"], ["la", "cliente"], ["la", "societe"], ["l'", "entreprise"], ["client"], ["cliente"], ["societe"]],
    };

    public static VoiceVocabulary English { get; } = new()
    {
        Fillers =
        [
            ["lettra"], ["please"], ["can", "you"], ["could", "you"], ["i", "want", "to"], ["i'", "d", "like", "to"], ["i", "would", "like", "to"],
            ["thanks"], ["thank", "you"], ["ok"], ["okay"], ["so"], ["um"], ["uh"],
        ],
        NavigateVerbs = [["open"], ["show"], ["display"], ["go", "to"], ["go", "back", "to"], ["view"], ["list"], ["see"]],
        NavigateLinks = ["the", "my", "me", "to", "all", "of"],
        CreateVerbs = [["new"], ["create"], ["make"], ["start"], ["prepare"]],
        Articles = ["a", "an", "the", "new"],
        SearchVerbs = [["search", "for"], ["search"], ["find"], ["look", "for"], ["look", "up"]],
        AddVerbs = [["add"], ["put"], ["insert"]],
        LineWords = [["a", "line", "with"], ["a", "line", "for"], ["a", "line"], ["line"]],
        DiscountWords = [["discount"], ["reduction"], ["rebate"]],
        DiscountVerbs = [["apply"], ["give"], ["add"], ["set"]],
        For = ["for"],
        Of = ["of"],
        PricePrepositions = [["at", "a", "price", "of"], ["at"], ["for"]],
        PerUnit = [["per", "hour"], ["an", "hour"], ["per", "day"], ["a", "day"], ["per", "unit"], ["each"], ["per", "month"], ["a", "month"]],
        LineSeparators = ["then", "plus", "and"],
        Commands =
        [
            new(["validate"], VoiceCommandKind.Validate), new(["finalize"], VoiceCommandKind.Validate), new(["finalise"], VoiceCommandKind.Validate),
            new(["save"], VoiceCommandKind.Save),
            new(["cancel"], VoiceCommandKind.Cancel), new(["close"], VoiceCommandKind.Cancel),
            new(["help"], VoiceCommandKind.Help), new(["what", "can", "i", "say"], VoiceCommandKind.Help),
        ],
        Targets =
        [
            new(["dashboard"], VoiceTarget.Home), new(["home"], VoiceTarget.Home),
            new(["quotes"], VoiceTarget.Quotes), new(["quote"], VoiceTarget.Quotes), new(["estimates"], VoiceTarget.Quotes), new(["estimate"], VoiceTarget.Quotes),
            new(["invoices"], VoiceTarget.Invoices), new(["invoice"], VoiceTarget.Invoices),
            new(["credit", "notes"], VoiceTarget.CreditNotes), new(["credit", "note"], VoiceTarget.CreditNotes),
            new(["customers"], VoiceTarget.Customers), new(["customer"], VoiceTarget.Customers), new(["clients"], VoiceTarget.Customers), new(["client"], VoiceTarget.Customers),
            new(["suppliers"], VoiceTarget.Suppliers), new(["supplier"], VoiceTarget.Suppliers), new(["vendors"], VoiceTarget.Suppliers), new(["vendor"], VoiceTarget.Suppliers),
            new(["catalog"], VoiceTarget.Catalog), new(["catalogue"], VoiceTarget.Catalog), new(["items"], VoiceTarget.Catalog), new(["item"], VoiceTarget.Catalog),
            new(["products"], VoiceTarget.Catalog), new(["product"], VoiceTarget.Catalog), new(["services"], VoiceTarget.Catalog),
            new(["fec"], VoiceTarget.Fec),
            new(["alert", "centre"], VoiceTarget.Alerts), new(["alert", "center"], VoiceTarget.Alerts), new(["alerts"], VoiceTarget.Alerts),
            new(["synchronization"], VoiceTarget.Sync), new(["synchronisation"], VoiceTarget.Sync), new(["sync"], VoiceTarget.Sync),
            new(["settings"], VoiceTarget.Settings), new(["preferences"], VoiceTarget.Settings),
            new(["assistant"], VoiceTarget.Assistant),
        ],
        Filters =
        [
            new(["overdue"], VoiceFilter.Overdue), new(["late"], VoiceFilter.Overdue),
            new(["unpaid"], VoiceFilter.Unpaid), new(["outstanding"], VoiceFilter.Unpaid),
            new(["paid"], VoiceFilter.Paid),
            new(["drafts"], VoiceFilter.Draft), new(["draft"], VoiceFilter.Draft),
            new(["sent"], VoiceFilter.Sent),
            new(["accepted"], VoiceFilter.Accepted), new(["signed"], VoiceFilter.Accepted),
            new(["refused"], VoiceFilter.Refused), new(["rejected"], VoiceFilter.Refused), new(["declined"], VoiceFilter.Refused),
            new(["expired"], VoiceFilter.Expired),
        ],
        Units =
        [
            new(["hours"], "HUR"), new(["hour"], "HUR"), new(["h"], "HUR"),
            new(["days"], "DAY"), new(["day"], "DAY"),
            new(["units"], "C62"), new(["unit"], "C62"), new(["pieces"], "C62"), new(["piece"], "C62"),
            new(["flat", "fees"], "LS"), new(["flat", "fee"], "LS"), new(["lump", "sum"], "LS"),
            new(["square", "meters"], "MTK"), new(["square", "metres"], "MTK"), new(["square", "meter"], "MTK"), new(["square", "metre"], "MTK"),
            new(["cubic", "meters"], "MTQ"), new(["cubic", "metres"], "MTQ"),
            new(["kilometers"], "KMT"), new(["kilometres"], "KMT"), new(["km"], "KMT"),
            new(["meters"], "MTR"), new(["metres"], "MTR"), new(["meter"], "MTR"), new(["metre"], "MTR"),
            new(["kilograms"], "KGM"), new(["kilos"], "KGM"), new(["kilo"], "KGM"), new(["kg"], "KGM"),
            new(["liters"], "LTR"), new(["litres"], "LTR"), new(["liter"], "LTR"), new(["litre"], "LTR"),
            new(["months"], "MON"), new(["month"], "MON"),
        ],
        CustomerPrefixes = [["the", "customer"], ["the", "client"], ["customer"], ["client"]],
    };

    public static VoiceVocabulary ForLanguage(SpokenLanguage language) => language == SpokenLanguage.English ? English : French;

    public required string[][] Fillers { get; init; }

    public required string[][] NavigateVerbs { get; init; }

    public required string[] NavigateLinks { get; init; }

    public required string[][] CreateVerbs { get; init; }

    public required string[] Articles { get; init; }

    public required string[][] SearchVerbs { get; init; }

    public required string[][] AddVerbs { get; init; }

    public required string[][] LineWords { get; init; }

    public required string[][] DiscountWords { get; init; }

    public required string[][] DiscountVerbs { get; init; }

    public required string[] For { get; init; }

    public required string[] Of { get; init; }

    public required string[][] PricePrepositions { get; init; }

    public required string[][] PerUnit { get; init; }

    public required string[] LineSeparators { get; init; }

    public required Phrase<VoiceCommandKind>[] Commands { get; init; }

    public required Phrase<VoiceTarget>[] Targets { get; init; }

    public required Phrase<VoiceFilter>[] Filters { get; init; }

    public required Phrase<string>[] Units { get; init; }

    public required string[][] CustomerPrefixes { get; init; }
}
