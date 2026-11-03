using static Lettra.UI.Shared.Demo.DemoAction;

namespace Lettra.UI.Shared.Demo;

/// <summary>
/// Le scénario de la démo commentée, décrit en données : un parcours complet d'une petite entreprise, du premier client
/// à la déclaration de TVA, puis la vue du cabinet comptable. Les textes des bulles sont dans
/// <c>Localization/Modules/DemoCommentee[.en].resx</c>, écrits pour quelqu'un qui ne connaît pas la comptabilité.
/// Les gestes visent les <c>id</c> que les écrans portent déjà (les mêmes que les tests) ; un écran qui en change
/// doit mettre ce scénario à jour (DemoScriptCatalogTests vérifie les textes, le parcours e2e vérifie les gestes).
/// </summary>
public static class DemoScriptCatalog
{
    /// <summary>Le client créé par la démo, retrouvé ensuite par le devis et l'encaissement.</summary>
    public const string CustomerName = "Boulangerie Dupain";

    /// <summary>Prix HT de l'article créé (450 €) et quantité du devis (2) : 900 € HT, 1 080 € TTC.</summary>
    public const string AmountInclTax = "1080";

    public static IReadOnlyList<DemoChapter> Chapters { get; } =
    [
        new("welcome", "circle-play",
        [
            new("intro", null, Go(""), Wait("#kpi-revenue, #home-empty")),
            new("dossier", "#dossier-picker"),
            new("revenue", "#kpi-revenue"),
            new("menu", ".lt-sidebar nav, .lt-tabbar"),
        ]),
        new("company", "building-2",
        [
            new("identity", "#company-siren", Go("settings/company"), Wait("#company-siren")),
            new("vat", "#company-vat"),
            new("terms", "#company-terms"),
        ]),
        new("customer", "users",
        [
            new("list", "#customers-list", Go("customers"), Wait("#customers-list")),
            new("new", "#name", Click("#customers-new"), Type("#name", CustomerName)),
            new("siren", "#siren", Type("#siren", "{siren}")),
            new("address", "#line1",
                Type("#email", "contact@boulangerie-dupain.example"), Type("#line1", "res:Demo.Value.Street"),
                Type("#zip", "69003"), Type("#city", "Lyon")),
            new("saved", "#customer-sheet", Click("#explorer-save"), Wait($"#customer-sheet-title >> {CustomerName}")),
        ]),
        new("catalog", "package",
        [
            new("list", "#catalog-items-list", Go("catalog"), Wait("#catalog-items-list, #catalog-items-empty")),
            new("new", "#item-designation",
                Click("#catalog-new-item"), Type("#item-reference", "ATELIER-{run}"), Type("#item-designation", "res:Demo.Value.Item")),
            new("price", "#item-price-incl", Type("#item-price", "450")),
            new("saved", "#catalog-sheet", Click("#explorer-save"), Wait("#catalog-sheet")),
        ]),
        new("quote", "file-text",
        [
            new("open", "#customer",
                Go("customers"), Wait("#customers-list"), Type("#customers-search", "Dupain"),
                Click($"#customers-list [role=option] >> {CustomerName}"), Click("#customer-new-quote"), Wait("#subject")),
            new("subject", "#subject", Type("#subject", "res:Demo.Value.Subject")),
            new("line", ".lettra-lines-host",
                Click(".lg-fkey >> Insert"),
                Type(".lettra-lines-host input[data-r='0'][data-c='0']", "ATELIER-{run}"),
                Type(".lettra-lines-host input[data-r='0'][data-c='2']", "2")),
            new("totals", "#totals-title", Wait("#total-incl-tax")),
            new("compliance", ".lettra-checklist", Wait("#validate:not([disabled])")),
            new("validate", "h1", Click("#validate"), Wait("#accept")),
            new("accept", "#final-invoice", Click("#accept"), Wait("#final-invoice")),
        ]),
        new("invoice", "receipt",
        [
            new("draft", "#lines-title", Click("#final-invoice"), Wait("#validate:not([disabled])")),
            new("validate", "h1", Click("#validate"), Wait("#issue")),
            new("qrcode", "#payment-qr", Wait("#payment-qr")),
            new("facturx", "#facturx"),
            new("issue", "#platform", Click("#issue"), Click("#platform-transmit"), Wait("#platform-status")),
        ], ContinuesPrevious: true),
        new("payment", "hand-coins",
        [
            new("new", "#payment-customer", Go("payments/new"), Type("#payment-customer", CustomerName)),
            new("allocate", "#payment-invoices",
                Type("#payment-amount", AmountInclTax), Type("#payment-reference", "res:Demo.Value.Transfer")),
            new("lettering", "#payment-lettering", Click("#payment-save"), Wait("#payment-lettering")),
        ]),
        new("bank", "landmark",
        [
            new("import", "#bank-stats", Go("bank"), Click("#bank-demo"), Wait("#bank-lines")),
            new("match", "#bank-lines"),
        ]),
        new("reminders", "mail",
        [
            new("overview", "#reminders-stats", Go("reminders"), Wait("#reminders-stats")),
            new("preview", "#reminders-preview", Click("[id^='reminders-open-']"), Wait("#reminders-preview")),
        ]),
        new("accounting", "book-open",
        [
            new("journal", "#journal-table", Go("accounting/journal"), Wait("#journal-table")),
            new("balance", "#balance-table", Click("#tab-balance"), Wait("#balance-table")),
        ]),
        new("tax", "calendar-clock",
        [
            new("vat", "#vat-stats", Go("tax/vat"), Wait("#vat-stats")),
            new("checks", "#vat-checks"),
            new("calendar", "[id^='calendar-month-']", Click("#tab-calendar"), Wait("[id^='calendar-month-']")),
        ]),
        new("fec", "file-spreadsheet",
        [
            new("export", "#fec-export", Go("exports/fec"), Wait("#fec-export")),
        ]),
        new("cabinet", "layers",
        [
            new("overview", "#cabinet-list-title", Go("cabinet"), Wait("#cabinet-list-title")),
            new("prevention", ".lt-prevention-kpis, #prevention-radar-empty", Go("cabinet/prevention"), Wait(".lt-prevention-kpis, #prevention-radar-empty")),
            new("pilotage", "#pilotage-dso", Go("pilotage"), Wait("#pilotage-dso")),
        ]),
        new("end", "circle-check",
        [
            new("done", null, Go(""), Wait("#kpi-revenue, #home-empty")),
        ]),
    ];

    public static DemoChapter? Find(string id) => Chapters.FirstOrDefault(c => c.Id == id);
}
