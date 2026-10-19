using Lettra.Contracts.Administration;
using Microsoft.AspNetCore.Components.Routing;

namespace Lettra.UI.Shared.Layout;

/// <summary>
/// Une entrée de la navigation : lien relatif, clé de libellé, icône Lucide, groupe et place dans les onglets du téléphone.
/// <paramref name="Page"/>, facultatif, cache l'entrée à qui n'ouvre pas cette page (lu une fois par
/// <c>IAdministrationGateway.GetMyAccessAsync</c>) ; sans lui, l'entrée est toujours affichée et le serveur décide.
/// </summary>
public sealed record NavItem(string Href, string LabelKey, string Icon, string? GroupKey, bool InTabBar = false, NavLinkMatch Match = NavLinkMatch.Prefix, AccessAreaDto? Page = null)
{
    /// <summary>Mots que la recherche du menu reconnaît en plus du libellé (« TVA » pour Déclarations), facultatifs.</summary>
    public string KeywordsKey => LabelKey + ".Keywords";

    /// <summary>L'entrée mène-t-elle à l'écran affiché ? <paramref name="path"/> est relatif à la base, sans requête.</summary>
    public bool IsCurrent(string path)
    {
        string current = path.Split('?', '#')[0].Trim('/');
        return Match == NavLinkMatch.All || Href.Length == 0
            ? string.Equals(current, Href, StringComparison.OrdinalIgnoreCase)
            : current.Equals(Href, StringComparison.OrdinalIgnoreCase) || current.StartsWith(Href + "/", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// La navigation de l'application, décrite une seule fois : barre latérale (ordinateur), rail (tablette), onglets et
/// feuille « Plus » (téléphone). Un nouvel écran ajoute sa ligne ici, avec sa clé <c>Nav.*</c> dans les ressources.
/// </summary>
public static class NavItems
{
    /// <summary>Section « Favoris » : les écrans épinglés par l'utilisateur, en tête du menu.</summary>
    public const string FavoritesGroupKey = "Nav.Group.Favorites";

    public static IReadOnlyList<NavItem> All { get; } =
    [
        new("", "Nav.Home", "layout-dashboard", null, InTabBar: true, Match: NavLinkMatch.All),
        new("cabinet", "Nav.Cabinet", "layers", null),
        new("quotes", "Nav.Quotes", "file-text", "Nav.Group.Sales", InTabBar: true),
        new("invoices", "Nav.Invoices", "receipt", "Nav.Group.Sales", InTabBar: true),
        new("credit-notes", "Nav.CreditNotes", "file-minus", "Nav.Group.Sales"),
        new("recurring-invoices", "Nav.RecurringInvoices", "repeat", "Nav.Group.Sales"),
        new("customers", "Nav.Customers", "users", "Nav.Group.Parties", InTabBar: true),
        new("suppliers", "Nav.Suppliers", "truck", "Nav.Group.Parties"),
        new("catalog", "Nav.Catalog", "package", "Nav.Group.Parties"),
        new("payments", "Nav.Payments", "hand-coins", "Nav.Group.Accounting"),
        new("bank", "Nav.Bank", "landmark", "Nav.Group.Accounting"),
        new("reminders", "Nav.Reminders", "mail", "Nav.Group.Accounting"),
        new("accounting", "Nav.Accounting", "book-open", "Nav.Group.Accounting"),
        new("exports/fec", "Nav.Fec", "file-spreadsheet", "Nav.Group.Accounting"),
        new("tax", "Nav.Tax", "calendar-clock", "Nav.Group.Accounting"),
        new("review", "Nav.Review", "list-checks", "Nav.Group.Accounting"),
        new("pilotage", "Nav.Pilotage", "chart-column", "Nav.Group.Settings"),
        new("alerts", "Nav.Alerts", "bell", "Nav.Group.Settings"),
        new("settings/company", "Nav.Company", "building-2", "Nav.Group.Settings", Match: NavLinkMatch.All),
        new("settings/preferences", "Nav.Preferences", "sliders-horizontal", "Nav.Group.Settings"),
        new("admin", "Nav.Admin", "shield-check", "Nav.Group.Settings", Page: AccessAreaDto.Administration),
        new("session/serveur", "Nav.SharedServer", "server", "Nav.Group.Settings"),
    ];

    /// <summary>Les entrées par groupe, dans l'ordre de déclaration (le groupe sans titre en premier).</summary>
    public static IEnumerable<IGrouping<string?, NavItem>> Groups(IEnumerable<NavItem> items) => items.GroupBy(i => i.GroupKey);
}
