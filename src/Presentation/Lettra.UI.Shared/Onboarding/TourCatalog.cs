namespace Lettra.UI.Shared.Onboarding;

/// <summary>
/// Les parcours guidés de Lettra, décrits en données : un écran, ses étapes, les éléments montrés. Le texte de chaque
/// étape est dans <c>Localization/Modules/Accompagnement[.en].resx</c>. Les cibles visent d'abord un attribut
/// <c>data-tour</c>, puis les <c>id</c> que les écrans portent déjà ; une cible absente n'arrête pas le parcours.
/// </summary>
public static class TourCatalog
{
    /// <summary>Parcours de l'assistant IA, lancé à sa première ouverture (et seulement si l'IA est activée).</summary>
    public const string AssistantTourId = "assistant";

    public static IReadOnlyList<TourDefinition> All { get; } =
    [
        new("home", 1, "",
        [
            new("welcome"),
            new("nav", "[data-tour='nav']", ".lt-sidebar nav", ".lt-tabbar"),
            new("search", "[data-tour='search']", "#palette-open"),
            new("starter", "[data-tour='starter']", "#starter-checklist"),
            new("appearance", "[data-tour='appearance']", "#appearance-toggle", "#tab-more"),
            new("help", "[data-tour='help']", "#tour-help"),
        ]),
        new("quotes", 1, "quotes",
        [
            new("list", "[data-tour='quotes-list']", "#quotes-list-title"),
            new("filters", "[data-tour='quotes-filters']", "#quotes-search", "#quotes-status"),
            new("new", "[data-tour='quotes-new']", "#quotes-new"),
        ]),
        new("quote-editor", 1, "quotes/*",
        [
            new("customer", "[data-tour='customer']", "#customer"),
            new("lines", "[data-tour='lines']", "#lines-title", ".lettra-lines-host"),
            new("keys", "[data-tour='function-keys']", ".lg-fkeys", ".lettra-lines-host"),
            new("quick", "[data-tour='lines']", ".lettra-lines-host"),
            new("templates", "[data-tour='notes']", "#notes"),
            new("compliance", "[data-tour='compliance']", "#checklist-title"),
            new("why", "[data-tour='compliance']", "#checklist-title"),
            new("validate", "[data-tour='validate']", "#validate"),
        ]),
        new("invoices", 1, "invoices",
        [
            new("list", "[data-tour='invoices-list']", "#invoices-list-title"),
            new("filters", "[data-tour='invoices-filters']", "#invoices-kind", "#invoices-status"),
            new("new", "[data-tour='invoices-new']", "#invoices-new"),
        ]),
        new("invoice-editor", 1, "invoices/*",
        [
            new("customer", "[data-tour='customer']", "#customer"),
            new("terms", "[data-tour='payment-terms']", "#payment-terms"),
            new("lines", "[data-tour='lines']", "#lines-title", ".lettra-lines-host"),
            new("compliance", "[data-tour='compliance']", "#checklist-title"),
            new("validate", "[data-tour='validate']", "#validate"),
            new("einvoice", "[data-tour='einvoice']", "#facturx", "#pdf"),
        ]),
        new("customers", 1, "customers",
        [
            new("lookup", "[data-tour='customer-lookup']", "#customer-lookup"),
            new("address", "[data-tour='customer-address']", "#zip"),
            new("list", "[data-tour='customers-list']", "#list-title"),
        ]),
        new("catalog", 1, "catalog",
        [
            new("list", "[data-tour='catalog-list']", "#catalog-list-title"),
            new("search", "[data-tour='catalog-search']", "#catalog-search"),
            new("new", "[data-tour='catalog-new']", "#catalog-new"),
        ]),
        new("fec", 1, "exports/fec",
        [
            new("period", "[data-tour='fec-period']", "#fec-period-title", "#fec-from"),
            new("export", "[data-tour='fec-export']", "#fec-export"),
            new("report", "[data-tour='fec-report']", "#fec-report-title", "#fec-export"),
        ]),
        new("sync", 1, "synchronisation",
        [
            new("state", "[data-tour='sync-state']", "#sync-state-title", "#sync-unavailable"),
            new("pending", "[data-tour='sync-pending']", "#sync-pending-title"),
            new("run", "[data-tour='sync-run']", "#sync-run"),
        ]),
        new("alerts", 1, "alerts",
        [
            new("counters", "[data-tour='alerts-counters']", "#counters-title"),
            new("list", "[data-tour='alerts-list']", "#list-title"),
            new("detail", "[data-tour='alerts-detail']", "#detail-title", "#list-title"),
        ]),
        new(AssistantTourId, 1, null,
        [
            new("intro", "[data-tour='assistant']", "#assistant-title"),
            new("feature", "[data-tour='assistant-feature']", "#assistant-feature"),
            new("ask", "[data-tour='assistant-question']", "#assistant-question"),
            new("limits", "[data-tour='assistant']", "#assistant-title"),
        ]),
    ];

    /// <summary>Le parcours de l'écran affiché, s'il y en a un.</summary>
    public static TourDefinition? ForPath(string path) => All.FirstOrDefault(t => t.Matches(path));

    public static TourDefinition? Find(string id) => All.FirstOrDefault(t => t.Id == id);
}
