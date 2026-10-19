using System.Globalization;
using Bunit;
using Lettra.Contracts.Alerts;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Identity;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Layout;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages;
using Lettra.UI.Shared.Pages.Accounting;
using Lettra.UI.Shared.Pages.Billing;
using Lettra.UI.Shared.Pages.CatalogItems;
using Lettra.UI.Shared.Pages.Parties;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Billing;
using Lettra.UI.Tests.Cabinet;
using Lettra.UI.Tests.Consultation;
using Lettra.UI.Tests.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Design;

/// <summary>
/// Galerie des écrans pour les captures du système de design (docs/design/systeme-de-design.md) : chaque écran est
/// rendu dans la coque avec des données fictives, puis écrit en HTML statique, une fois par thème. Ne produit rien sans
/// la variable <c>LETTRA_GALLERY_DIR</c> ; <c>build/design-captures.cjs</c> photographie ensuite chaque page à 375, 768
/// et 1440 px et passe l'audit axe.
/// </summary>
[Collection("culture")]
public sealed class GalleryTests : BunitContext
{
    private static readonly string[] Themes = ["light", "dark", "slate", "contrast"];

    private static readonly string[] Clients =
        ["Boulangerie Martin", "Garage des Lilas", "Studio Nord", "Atelier Coste", "Pharmacie du Port", "Cabinet Roux"];

    private static readonly string[] Villes = ["Lyon", "Paris", "Nantes", "Bordeaux", "Lille", "Rennes"];

    private readonly FakeInvoicesGateway _invoices = new();
    private readonly FakeQuotesGateway _quotes = new();
    private readonly FakeCustomersGateway _customers = new();
    private readonly FakeAccountingGateway _accounting = new();
    private readonly FakeCatalogGateway _catalog = new();
    private readonly FakeAlertsGateway _alerts = new();

    public GalleryTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddCabinetFakes();
        Services.AddSingleton<IInvoicesGateway>(_invoices);
        Services.AddSingleton<IQuotesGateway>(_quotes);
        Services.AddSingleton<ICustomersGateway>(_customers);
        Services.AddSingleton<IAccountingGateway>(_accounting);
        Services.AddSingleton<ISuppliersGateway>(new FakeSuppliersGateway());
        Services.AddSingleton<ICatalogGateway>(_catalog);
        Services.AddSingleton<IDirectoryGateway>(new FakeDirectoryGateway());
        Services.AddSingleton<IBillingSettingsGateway>(new FakeBillingSettingsGateway());
        Services.AddSingleton<IPlatformTransmissionGateway>(new FakePlatformTransmissionGateway(_invoices));
        Services.AddSingleton<IReceivablesGateway>(new FakeReceivablesGateway(new ReceivablesSummaryResponse(
            new DateOnly(2026, 10, 16), 4, 18_960m,
            [
                new OverdueInvoiceResponse(Guid.CreateVersion7(), "FAC-2026-0031", "Atelier Durand", new DateOnly(2026, 8, 14), 63, 7_200m),
                new OverdueInvoiceResponse(Guid.CreateVersion7(), "FAC-2026-0038", "Boulangerie Petit", new DateOnly(2026, 9, 2), 44, 2_880m),
                new OverdueInvoiceResponse(Guid.CreateVersion7(), "FAC-2026-0044", "Cabinet Moreau", new DateOnly(2026, 9, 21), 25, 5_400m),
                new OverdueInvoiceResponse(Guid.CreateVersion7(), "FAC-2026-0049", "Garage Lefèvre", new DateOnly(2026, 10, 9), 7, 3_480m),
            ],
            3, 11, 2, 26)));
        Services.AddSingleton<IFileDownloader>(new FakeFileDownloader());
        Services.AddSingleton<IAlertsGateway>(_alerts);
        Services.AddSingleton<ISessionGateway>(new FakeSessionGateway(new SessionResponse(true, SessionModeDto.Local, "Claire Martin", true)));
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    [Fact]
    public void Galerie_des_ecrans_pour_les_captures()
    {
        string? dossier = Environment.GetEnvironmentVariable("LETTRA_GALLERY_DIR");
        if (string.IsNullOrEmpty(dossier))
        {
            return;
        }

        Directory.CreateDirectory(dossier);
        Guid facture = Donnees();
        Ecrire(dossier, "accueil", "", Page<Home>());
        Ecrire(dossier, "accueil-apparence", "", Page<Home>(), "#appearance-toggle");
        Ecrire(dossier, "accueil-plus", "", Page<Home>(), "#tab-more");
        Ecrire(dossier, "devis", "quotes", Page<QuoteList>());
        Ecrire(dossier, "factures", "invoices", Page<InvoiceList>());
        Ecrire(dossier, "facture", $"invoices/{facture}", b =>
        {
            b.OpenComponent<InvoiceEditor>(0);
            b.AddComponentParameter(1, nameof(InvoiceEditor.Id), facture);
            b.CloseComponent();
        });
        Ecrire(dossier, "clients", "customers", Page<CustomersPage>());
        Ecrire(dossier, "client", $"customers/{_customers.Customers[1].Id}", Page<CustomersPage>());
        Ecrire(dossier, "client-saisie", $"customers/{_customers.Customers[1].Id}/edit", Page<CustomersPage>());
        Ecrire(dossier, "articles", "catalog", Page<CatalogPage>());
        Ecrire(dossier, "article", $"catalog/{_catalog.Items[0].Id}", Page<CatalogPage>());
        Ecrire(dossier, "articles-grille", "catalog/grid", Page<Catalog>());
        Ecrire(dossier, "journal", "accounting/journal?from=2026-01-01&to=2026-12-31", Page<JournalPage>());
        Ecrire(dossier, "balance", "accounting/balance?from=2026-01-01&to=2026-12-31", Page<BalancePage>());
        Ecrire(dossier, "grand-livre", "accounting/accounts/411?from=2026-01-01&to=2026-12-31", b =>
        {
            b.OpenComponent<LedgerPage>(0);
            b.AddComponentParameter(1, nameof(LedgerPage.Account), "411");
            b.CloseComponent();
        });
        Guid alerte = _alerts.Add("integrite-chaine", AlertSeverity.S1).Alert.Id;
        _alerts.Add("integrite-numerotation", AlertSeverity.S2, AlertState.Acknowledged);
        Ecrire(dossier, "alertes", $"alerts/{alerte}", b =>
        {
            b.OpenComponent<Alerts>(0);
            b.AddComponentParameter(1, nameof(Alerts.Id), alerte);
            b.CloseComponent();
        });
        Ecrire(dossier, "entreprise", "settings/company", Page<CompanySettings>());
    }

    [Fact]
    public void Galerie_du_menu_et_de_l_aide_pour_les_captures()
    {
        string? dossier = Environment.GetEnvironmentVariable("LETTRA_GALLERY_DIR");
        if (string.IsNullOrEmpty(dossier))
        {
            return;
        }

        Directory.CreateDirectory(dossier);
        Donnees();
        Ecrire(dossier, "menu-regle", "invoices", Page<InvoiceList>(), coque =>
        {
            coque.Find("button[aria-label='Épingler Catalogue dans les favoris']").Click();
            coque.Find("button[aria-label='Épingler Rapprochement bancaire dans les favoris']").Click();
            coque.Find(".lt-sidebar [data-group='Nav.Group.Sales'] .lt-nav-group").Click();
            coque.Find(".lt-sidebar [data-group='Nav.Group.Parties'] .lt-nav-group").Click();
        });
        Ecrire(dossier, "menu-recherche", "", Page<Home>(), coque => coque.Find("#nav-filter").Input("tva"));
        Ecrire(dossier, "menu-recherche-vide", "", Page<Home>(), coque => coque.Find("#nav-filter").Input("Dupont"));
        Ecrire(dossier, "menu-plus-recherche", "", Page<Home>(), coque =>
        {
            coque.Find("#tab-more").Click();
            coque.Find("#more-filter").Input("fac");
        });
        Ecrire(dossier, "aide", "invoices", Page<InvoiceList>(), coque => coque.Find("#tour-help").Click());
        Ecrire(dossier, "aide-question", "", Page<Home>(), coque =>
        {
            coque.Find("#tour-help").Click();
            coque.Find("#help-query").Input("je veux annuler ma facture");
        });
        // En dernier : le repli en rail est retenu pour les pages suivantes.
        Services.GetRequiredService<Lettra.UI.Shared.Onboarding.HelpPanelState>().Close();
        Ecrire(dossier, "menu-rail", "", Page<Home>(), coque => coque.Find("#nav-collapse").Click());
    }

    [Fact]
    public void Galerie_d_un_dossier_vide_pour_les_captures()
    {
        string? dossier = Environment.GetEnvironmentVariable("LETTRA_GALLERY_DIR");
        if (string.IsNullOrEmpty(dossier))
        {
            return;
        }

        Directory.CreateDirectory(dossier);
        Services.AddSingleton<IDemoDataGateway>(new FakeDemoDataGateway(true, () => { }));
        Ecrire(dossier, "accueil-vide", "", Page<Home>());
        Ecrire(dossier, "devis-vide", "quotes", Page<QuoteList>());
    }

    [Fact]
    public void Galerie_des_preferences_pour_les_captures()
    {
        string? dossier = Environment.GetEnvironmentVariable("LETTRA_GALLERY_DIR");
        if (string.IsNullOrEmpty(dossier))
        {
            return;
        }

        Directory.CreateDirectory(dossier);
        Guid facture = Donnees();
        Guid boulangerie = FakeCabinetGateway.Boulangerie;
        DateTimeOffset maintenant = new(2026, 10, 16, 11, 0, 0, TimeSpan.Zero);
        Preferences.FakeUserPreferencesGateway compte = new()
        {
            Stored = new Contracts.Preferences.UserPreferencesResponse(
                new Contracts.Preferences.WorkspacePreferencesDto { HomePage = null },
                [new Contracts.Preferences.EntryDefaultsDto { DossierId = boulangerie, Unit = "HUR", QuoteValidityDays = 45, PaymentTerms = "Paiement à 30 jours fin de mois." }],
                [new Contracts.Preferences.DossierUsageDto(boulangerie,
                [
                    new(Contracts.Preferences.UsageKindDto.Invoice, facture, 3, maintenant),
                    new(Contracts.Preferences.UsageKindDto.Quote, _quotes.Quotes.Keys.First(), 1, maintenant.AddHours(-2)),
                    new(Contracts.Preferences.UsageKindDto.Customer, _customers.Customers[2].Id, 6, maintenant),
                ])],
                4,
                maintenant),
        };
        Services.AddSingleton<Lettra.UI.Abstractions.IUserPreferencesGateway>(compte);
        Ecrire(dossier, "mes-preferences", "settings/preferences", Page<Lettra.UI.Shared.Pages.Settings.MyPreferences>());
        Ecrire(dossier, "accueil-reprendre", "", Page<Home>());
    }

    private static RenderFragment Page<TPage>() where TPage : IComponent => b =>
    {
        b.OpenComponent<TPage>(0);
        b.CloseComponent();
    };

    private void Ecrire(string dossier, string nom, string route, RenderFragment page, string? clic = null) =>
        Ecrire(dossier, nom, route, page, coque =>
        {
            if (clic is not null)
            {
                coque.Find(clic).Click();
            }
        });

    private void Ecrire(string dossier, string nom, string route, RenderFragment page, Action<IRenderedComponent<MainLayout>> preparer)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(route);
        IRenderedComponent<MainLayout> coque = Render<MainLayout>(p => p.Add(l => l.Body, page));
        coque.WaitForState(() => coque.FindAll(".lt-skeleton, .lettra-loading").Count == 0, TimeSpan.FromSeconds(5));
        preparer(coque);

        foreach (string theme in Themes)
        {
            File.WriteAllText(Path.Combine(dossier, $"{nom}.{theme}.html"), $"""
                <!DOCTYPE html>
                <html lang="fr" data-theme="{theme}">
                <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover" />
                <title>{nom} · {theme}</title>
                <link rel="stylesheet" href="css/lettra.css" />
                <link rel="stylesheet" href="css/lettra-grid.css" />
                </head>
                <body>
                {coque.Markup}
                </body>
                </html>
                """);
        }
    }

    /// <summary>Six mois d'activité fictive ; rend l'identifiant d'une facture émise.</summary>
    private Guid Donnees()
    {
        static DocumentLineRequest Ligne(string designation, decimal quantite, decimal prix) =>
            new() { Designation = designation, UnitCode = "HUR", Quantity = quantite, UnitPriceExclTax = prix };

        foreach ((string nom, int i) in Clients.Select((n, i) => (n, i)))
        {
            _customers.Customers.Add(new CustomerSummary(Guid.CreateVersion7(), i % 3 == 2 ? CustomerKindDto.Individual : CustomerKindDto.Company,
                nom, i % 3 == 2 ? null : $"55203253{i}", Villes[i]));
        }

        decimal[] volumes = [7, 9, 6, 11, 10, 13];
        int numero = 0;
        Guid derniere = Guid.Empty;
        for (int mois = 0; mois < volumes.Length; mois++)
        {
            for (int n = 0; n < volumes[mois]; n++)
            {
                DateOnly date = new DateOnly(2026, 5, 1).AddMonths(mois).AddDays(Math.Min(n * 2, 15));
                InvoiceResponse facture = FakeInvoicesGateway.Build(InvoiceKindDto.Invoice, n % 4 == 0 ? InvoiceStatusDto.Issued : InvoiceStatusDto.Validated,
                    $"FAC-2026-{++numero:0000}", Ligne("Tenue comptable", 1 + (n % 3), 450m + (n * 35m)), Ligne("Conseil", 2, 120m));
                facture = facture with
                {
                    IssueDate = date,
                    Customer = facture.Customer with { Name = Clients[(n + mois) % Clients.Length] },
                };
                _invoices.Invoices[facture.Id] = facture;
                derniere = facture.Id;
                CustomerSummary client = _customers.Customers.Single(c => c.Name == facture.Customer.Name);
                _accounting.Entries.Add(FakeAccountingGateway.Invoice(facture.Number!, date, client.Id, client.Name, facture.Totals.TotalExclTax, facture.Totals.TotalVat));
            }
        }

        InvoiceResponse avoir = FakeInvoicesGateway.Build(InvoiceKindDto.CreditNote, InvoiceStatusDto.Validated, "AV-2026-0003", Ligne("Remise commerciale", 1, 300m));
        _invoices.Invoices[avoir.Id] = avoir with { IssueDate = new DateOnly(2026, 10, 12), Customer = avoir.Customer with { Name = Clients[1] } };
        _invoices.Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Draft, null, Ligne("Bilan annuel", 1, 1800m));

        QuoteStatusDto[] statuts = [QuoteStatusDto.Validated, QuoteStatusDto.Accepted, QuoteStatusDto.Validated, QuoteStatusDto.Refused, QuoteStatusDto.Validated];
        for (int n = 0; n < statuts.Length; n++)
        {
            QuoteResponse devis = _quotes.Add(statuts[n], $"DEV-2026-{n + 31:0000}", Ligne("Mission annuelle", 1, 2400m + (n * 600m)));
            _quotes.Quotes[devis.Id] = devis with
            {
                IssueDate = new DateOnly(2026, 10, 14 - (n * 3)),
                Customer = devis.Customer with { Name = Clients[(n + 2) % Clients.Length] },
            };
        }

        _quotes.Add(QuoteStatusDto.Draft, null, Ligne("Création de société", 1, 1500m));
        _catalog.Add("TENUE", "Tenue comptable mensuelle", 450m);
        _catalog.Add("BILAN", "Bilan annuel et liasse fiscale", 1800m);
        _catalog.Add("CONSEIL", "Conseil (heure)", 120m);
        _catalog.Add("PAIE", "Bulletin de paie", 25m, unit: "C62");
        _catalog.Add("ANCIEN", "Ancienne prestation", 80m, archived: true);
        return derniere;
    }
}
