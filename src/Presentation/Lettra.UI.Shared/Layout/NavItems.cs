using Microsoft.AspNetCore.Components.Routing;

namespace Lettra.UI.Shared.Layout;

/// <summary>Une entrée de la navigation : lien relatif, clé de libellé, icône Lucide, groupe et place dans les onglets du téléphone.</summary>
public sealed record NavItem(string Href, string LabelKey, string Icon, string? GroupKey, bool InTabBar = false, NavLinkMatch Match = NavLinkMatch.Prefix);

/// <summary>
/// La navigation de l'application, décrite une seule fois : barre latérale (ordinateur), rail (tablette), onglets et
/// feuille « Plus » (téléphone). Un nouvel écran ajoute sa ligne ici, avec sa clé <c>Nav.*</c> dans les ressources.
/// </summary>
public static class NavItems
{
    public static IReadOnlyList<NavItem> All { get; } =
    [
        new("", "Nav.Home", "layout-dashboard", null, InTabBar: true, Match: NavLinkMatch.All),
        new("quotes", "Nav.Quotes", "file-text", "Nav.Group.Sales", InTabBar: true),
        new("invoices", "Nav.Invoices", "receipt", "Nav.Group.Sales", InTabBar: true),
        new("credit-notes", "Nav.CreditNotes", "file-minus", "Nav.Group.Sales"),
        new("customers", "Nav.Customers", "users", "Nav.Group.Parties", InTabBar: true),
        new("suppliers", "Nav.Suppliers", "truck", "Nav.Group.Parties"),
        new("catalog", "Nav.Catalog", "package", "Nav.Group.Parties"),
        new("accounting", "Nav.Accounting", "book-open", "Nav.Group.Accounting"),
        new("exports/fec", "Nav.Fec", "file-spreadsheet", "Nav.Group.Accounting"),
        new("alerts", "Nav.Alerts", "bell", "Nav.Group.Settings"),
        new("settings/company", "Nav.Company", "building-2", "Nav.Group.Settings", Match: NavLinkMatch.All),
    ];

    /// <summary>Les entrées par groupe, dans l'ordre de déclaration (le groupe sans titre en premier).</summary>
    public static IEnumerable<IGrouping<string?, NavItem>> Groups(IEnumerable<NavItem> items) => items.GroupBy(i => i.GroupKey);
}
