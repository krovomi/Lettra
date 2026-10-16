namespace Lettra.DemoData;

/// <summary>Une ville et son code postal (les adresses, elles, sont inventées).</summary>
internal sealed record DemoCity(string PostalCode, string Name);

/// <summary>Un article ou une prestation type d'un métier, avant tirage du prix.</summary>
internal sealed record DemoItemTemplate(
    string Family,
    string Designation,
    string? Description,
    bool IsService,
    string UnitCode,
    decimal UnitPrice,
    string VatRateCode);

/// <summary>Listes maison (pas de bibliothèque de données factices) : noms, rues, villes, raisons sociales.</summary>
internal static class DemoReferenceData
{
    public static readonly string[] FirstNames =
    [
        "Camille", "Léa", "Manon", "Chloé", "Emma", "Inès", "Sarah", "Julie", "Laura", "Pauline", "Claire", "Anaïs", "Lucie", "Mathilde",
        "Élodie", "Margaux", "Nathalie", "Sophie", "Isabelle", "Hélène", "Lucas", "Hugo", "Louis", "Thomas", "Nicolas", "Julien",
        "Maxime", "Antoine", "Alexandre", "Romain", "Pierre", "Mathieu", "Guillaume", "Baptiste", "Olivier", "Philippe", "Laurent",
        "Sébastien", "Vincent", "Yann", "Karim", "Mehdi", "Samir", "Nadia", "Fatima", "Amina", "Kevin", "Jordan", "Gaëlle", "Solène",
    ];

    public static readonly string[] LastNames =
    [
        "Martin", "Bernard", "Dubois", "Thomas", "Robert", "Richard", "Petit", "Durand", "Leroy", "Moreau", "Simon", "Laurent",
        "Lefebvre", "Michel", "Garcia", "David", "Bertrand", "Roux", "Vincent", "Fournier", "Morel", "Girard", "André", "Lefèvre",
        "Mercier", "Dupont", "Lambert", "Bonnet", "François", "Martinez", "Legrand", "Garnier", "Faure", "Rousseau", "Blanc",
        "Guérin", "Muller", "Henry", "Roussel", "Nicolas", "Perrin", "Morin", "Mathieu", "Clément", "Gauthier", "Dumont", "Lopez",
        "Fontaine", "Chevalier", "Robin", "Masson", "Sanchez", "Gérard", "Nguyen", "Boyer", "Denis", "Lemaire", "Duval", "Joly",
        "Gautier", "Roger", "Roche", "Roy", "Noël", "Meyer", "Lucas", "Meunier", "Jean", "Perez", "Marchand", "Dufour", "Blanchard",
    ];

    public static readonly string[] StreetTypes = ["rue", "rue", "rue", "avenue", "boulevard", "place", "impasse", "allée", "chemin", "quai"];

    public static readonly string[] StreetNames =
    [
        "de la République", "Victor Hugo", "Jean Jaurès", "Pasteur", "de la Gare", "du Général de Gaulle", "des Lilas", "du Moulin",
        "de l'Église", "Gambetta", "Voltaire", "des Écoles", "de la Liberté", "du Château", "des Tilleuls", "Jules Ferry", "de Verdun",
        "du Commerce", "des Acacias", "de la Paix", "Carnot", "Émile Zola", "des Peupliers", "de Bretagne", "du Port", "des Vignes",
        "de la Fontaine", "Saint-Michel", "des Artisans", "de l'Industrie", "des Prés", "de la Mairie", "du Stade", "Lamartine",
    ];

    public static readonly DemoCity[] Cities =
    [
        new("75011", "Paris"), new("75015", "Paris"), new("75017", "Paris"), new("92100", "Boulogne-Billancourt"), new("93100", "Montreuil"),
        new("94300", "Vincennes"), new("69003", "Lyon"), new("69100", "Villeurbanne"), new("13006", "Marseille"), new("13100", "Aix-en-Provence"),
        new("31000", "Toulouse"), new("33000", "Bordeaux"), new("44000", "Nantes"), new("35000", "Rennes"), new("59000", "Lille"),
        new("67000", "Strasbourg"), new("34000", "Montpellier"), new("06000", "Nice"), new("38000", "Grenoble"), new("21000", "Dijon"),
        new("37000", "Tours"), new("49000", "Angers"), new("76000", "Rouen"), new("51100", "Reims"), new("63000", "Clermont-Ferrand"),
        new("87000", "Limoges"), new("29200", "Brest"), new("64000", "Pau"), new("30000", "Nîmes"), new("83000", "Toulon"),
        new("84000", "Avignon"), new("45000", "Orléans"), new("14000", "Caen"), new("25000", "Besançon"), new("57000", "Metz"),
    ];

    public static readonly string[] CompanyForms = ["SARL", "SAS", "SAS", "SASU", "EURL", "SA", "SCI", "SELARL"];

    public static readonly string[] CompanyNameStarts =
    [
        "Atelier", "Maison", "Groupe", "Cabinet", "Compagnie", "Société", "Les Ateliers", "Studio", "Comptoir", "Agence", "Bureau",
        "Laboratoire", "Établissements", "Domaine", "Ferme", "Garage", "Boulangerie", "Pharmacie", "Clinique", "Hôtel", "Restaurant",
    ];

    public static readonly string[] CompanyNameEnds =
    [
        "du Lac", "des Cèdres", "Saint-Roch", "de l'Ouest", "Horizon", "Azur", "Lumière", "des Halles", "Belle Rive", "du Parc",
        "Les Pins", "Bel Air", "de la Vallée", "du Centre", "Montaigne", "Colbert", "Mistral", "Océane", "Grand Sud", "des Alpes",
        "Boréal", "Vauban", "Pré Vert", "de la Loire", "Sainte-Anne", "Rive Gauche", "Iris", "Hermine", "Garonne", "Cévennes",
    ];

    /// <summary>Saisonnalité de l'activité (janvier à décembre) : creux d'août, rentrée et fin d'année chargées.</summary>
    public static readonly double[] MonthlyActivity = [0.9, 1.0, 1.1, 1.0, 1.0, 1.05, 0.8, 0.45, 1.2, 1.25, 1.2, 1.0];
}
