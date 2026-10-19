using Lettra.UI.Shared.Components.Palette;

namespace Lettra.UI.Shared.Onboarding;

/// <summary>
/// Une question fréquente de l'aide. Les textes sont dans <c>Localization/Modules/Aide[.en].resx</c> sous
/// <c>Help.Faq.&lt;Id&gt;.Question</c>, <c>.Answer</c> et <c>.Keywords</c> (mots que l'on tape quand on cherche).
/// </summary>
/// <param name="Id">Identifiant stable (clés de ressources).</param>
/// <param name="Routes">Écrans où la question remonte en tête (« Sur cet écran »), au format des parcours :
/// exact (<c>invoices</c>) ou préfixe suivi de <c>/*</c> (<c>invoices/*</c>).</param>
/// <param name="LinkHref">Écran qui répond à la question, ouvert par le bouton sous la réponse.</param>
/// <param name="LinkLabelKey">Libellé de ce bouton (en général la clé <c>Nav.*</c> de l'écran).</param>
public sealed record HelpEntry(string Id, IReadOnlyList<string> Routes, string? LinkHref = null, string? LinkLabelKey = null)
{
    public string QuestionKey => $"Help.Faq.{Id}.Question";

    public string AnswerKey => $"Help.Faq.{Id}.Answer";

    public string KeywordsKey => $"Help.Faq.{Id}.Keywords";

    public bool AppliesTo(string path) =>
        Routes.Any(route => new TourDefinition(Id, 1, route, []).Matches(path));
}

/// <summary>
/// L'aide de Lettra sans IA : des réponses écrites à l'avance aux questions que l'on se pose en s'y perdant, cherchées
/// sans accents ni casse dans la question, la réponse et les mots-clés. Ajouter une question : une ligne ici et ses
/// trois textes dans les ressources (français et anglais).
/// </summary>
public static class HelpCatalog
{
    public static IReadOnlyList<HelpEntry> All { get; } =
    [
        new("first-quote", ["", "quotes", "quotes/*"], "quotes/new", "Home.NewQuote"),
        new("quote-to-invoice", ["quotes", "quotes/*", "invoices"], "quotes", "Nav.Quotes"),
        new("fix-invoice", ["invoices", "invoices/*", "credit-notes"], "invoices", "Nav.Invoices"),
        new("payment", ["payments", "payments/*", "bank", "invoices/*"], "payments/new", "Payments.New"),
        new("reminder", ["reminders", "invoices", "payments"], "reminders", "Nav.Reminders"),
        new("recurring", ["recurring-invoices", "recurring-invoices/*", "invoices"], "recurring-invoices", "Nav.RecurringInvoices"),
        new("customer", ["customers", "customers/*", "suppliers", "suppliers/*"], "customers/new", "Nav.Customers"),
        new("vat", ["tax", "tax/*"], "tax", "Nav.Tax"),
        new("fec", ["exports/fec", "accounting", "accounting/*", "tax/*"], "exports/fec", "Nav.Fec"),
        new("dossier", ["", "cabinet", "review", "review/*"], "cabinet", "Nav.Cabinet"),
        new("team", ["admin"], "admin", "Nav.Admin"),
        new("devices", ["session/serveur", "synchronisation", "synchronisation/*"], "session/serveur", "Nav.SharedServer"),
        new("offline", ["synchronisation", "synchronisation/*", "session/serveur"]),
        new("search", [""]),
        new("menu", [""]),
        new("voice", ["quotes/*", "invoices/*"]),
        new("appearance", [""]),
        new("data", ["", "settings/company"]),
        new("assistant", []),
    ];

    public static HelpEntry? Find(string id) => All.FirstOrDefault(e => e.Id == id);

    /// <summary>Les questions de l'écran affiché, dans l'ordre du catalogue.</summary>
    public static IReadOnlyList<HelpEntry> ForPath(string path) => [.. All.Where(e => e.AppliesTo(path))];

    /// <summary>
    /// Les questions qui répondent à <paramref name="query"/>, les meilleures d'abord. La question est posée en langage
    /// courant (« je veux annuler ma facture ») : chaque mot utile compte, sans accents ni casse, par son début
    /// (« annul » trouve « annulation ») ; un mot trouvé dans la question pèse plus que dans les mots-clés, puis dans la
    /// réponse. Une requête vide rend tout le catalogue. <paramref name="everyWord"/> exige que chaque mot utile soit
    /// trouvé (palette Ctrl+K, où « avoir 0042 » cherche une pièce et non une question).
    /// </summary>
    public static IReadOnlyList<HelpEntry> Search(string? query, Func<string, string> text, bool everyWord = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] words = Words(query);
        if (words.Length == 0)
        {
            return string.IsNullOrWhiteSpace(query) ? All : [];
        }

        List<(HelpEntry Entry, int Index, int Score)> scored = All
            .Select((entry, index) => (entry, index, score: Score(entry, words, text, everyWord)))
            .Where(m => m.score > 0)
            .ToList();
        int best = scored.Count == 0 ? 0 : scored.Max(m => m.Score);

        // Les réponses qui ne prennent qu'un mot sur deux de la meilleure sont du bruit : « annuler ma facture » ne doit
        // pas lister toutes les questions qui parlent de facture.
        return [.. scored
            .Where(m => m.Score == best || m.Score * 2 > best)
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Index)
            .Select(m => m.Entry)];
    }

    /// <summary>Mots utiles d'une question, ramenés à leur début : sans les petits mots (« comment », « une », « je »).</summary>
    public static string[] Words(string? query) =>
    [
        .. PaletteMatcher.Normalize(query ?? string.Empty)
            .Split([' ', '-', '\''], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && !StopWords.Contains(w))
            .Select(w => w.Length > 5 ? w[..5] : w)
            .Distinct(StringComparer.Ordinal),
    ];

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "comment", "pourquoi", "quoi", "quel", "quelle", "quels", "quelles", "une", "des", "les", "est", "que", "qui",
        "pour", "par", "sur", "dans", "avec", "mon", "mes", "ma", "ton", "son", "ses", "veux", "voudrais", "faire",
        "faut", "peux", "puis", "pas", "plus", "how", "what", "why", "the", "and", "for", "can", "with", "want",
        "does", "where", "need", "you", "your", "est-ce", "cette", "ces", "aux", "elle", "il", "nous", "vous",
    };

    private static int Score(HelpEntry entry, string[] words, Func<string, string> text, bool everyWord)
    {
        string question = " " + PaletteMatcher.Normalize(text(entry.QuestionKey));
        string keywords = " " + PaletteMatcher.Normalize(text(entry.KeywordsKey));
        string answer = " " + PaletteMatcher.Normalize(text(entry.AnswerKey));
        int score = 0;
        foreach (string word in words)
        {
            int found = question.Contains(word, StringComparison.Ordinal) ? 6
                : keywords.Contains(word, StringComparison.Ordinal) ? 4
                : answer.Contains(word, StringComparison.Ordinal) ? 1
                : 0;
            if (found == 0 && everyWord)
            {
                return 0;
            }

            score += found;
        }

        return score;
    }
}
