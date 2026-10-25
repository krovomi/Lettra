using System.Globalization;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared.Billing;
using Lettra.UI.Shared.Keyboard;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Components.Palette;

/// <summary>Actions et écrans, plus les commandes de la page affichée (registre des raccourcis).</summary>
public sealed class NavigationPaletteProvider(
    NavigationManager navigation,
    ShortcutRegistry registry,
    UiPreferences preferences,
    IStringLocalizer<SharedResource> localizer) : ICommandPaletteProvider
{
    public Task<IReadOnlyList<PaletteItem>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        string actions = localizer["Palette.Group.Actions"];
        string screens = localizer["Palette.Group.Screens"];
        string page = localizer["Palette.Group.Page"];
        List<PaletteItem> items =
        [
            Go(actions, "Palette.NewQuote", "quotes/new"),
            Go(actions, "Palette.NewInvoice", "invoices/new"),
            new(actions, localizer["Palette.ToggleDensity"], preferences.ToggleDensityAsync, "Ctrl+Shift+D"),
            Go(screens, "Nav.Home", ""),
            Go(screens, "Nav.Quotes", "quotes"),
            Go(screens, "Nav.Invoices", "invoices"),
            Go(screens, "Nav.CreditNotes", "credit-notes"),
            Go(screens, "Nav.Customers", "customers"),
            Go(screens, "Nav.Suppliers", "suppliers"),
            Go(screens, "Nav.Catalog", "catalog"),
            Go(screens, "Nav.Journal", "accounting/journal"),
            Go(screens, "Nav.Balance", "accounting/balance"),
            Go(screens, "Nav.Fec", "exports/fec"),
            Go(screens, "Nav.Alerts", "alerts"),
            Go(screens, "Nav.Company", "settings/company"),
            Go(screens, "Nav.Preferences", "settings/preferences"),
        ];
        // Thèmes : au clavier comme depuis le menu Apparence.
        items.AddRange(Enum.GetValues<UiTheme>().Select(theme => new PaletteItem(
            actions,
            string.Format(CultureInfo.CurrentCulture, localizer["Palette.Theme"], localizer[$"Appearance.Theme.{theme}"]),
            () => preferences.SetThemeAsync(theme))));

        foreach (ShortcutBinding binding in registry.PaletteBindings.Where(b => b.Chord != CommandPalette.Shortcut))
        {
            items.Add(new PaletteItem(page, binding.Label, () => binding.Execute(CommandContext.None), binding.Chord.ToString()));
        }

        IReadOnlyList<PaletteItem> matches = items.Where(i => PaletteMatcher.Score(i.Label, query) > 0).ToList();
        return Task.FromResult(matches);
    }

    private PaletteItem Go(string group, string labelKey, string uri) =>
        new(group, localizer[labelKey], () =>
        {
            navigation.NavigateTo(uri);
            return Task.CompletedTask;
        });
}

/// <summary>
/// Pièces et clients : « F-2026-0042 » ouvre la facture, « avoir F-2026-0042 » prépare l'avoir, « facture Dupont »
/// propose une nouvelle facture pour ce client. Les gateways sont lues à la demande : sans elles, rien n'est proposé.
/// </summary>
public sealed class BillingPaletteProvider(
    Optional<ICustomersGateway> customerGateway,
    Optional<IQuotesGateway> quoteGateway,
    Optional<IInvoicesGateway> invoiceGateway,
    NavigationManager navigation,
    IStringLocalizer<SharedResource> localizer) : ICommandPaletteProvider
{
    private IReadOnlyList<CustomerSummary>? _customers;

    public async Task<IReadOnlyList<PaletteItem>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            return [];
        }

        List<PaletteItem> items = [];
        await AddCustomersAsync(items, cancellationToken).ConfigureAwait(false);
        string? number = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).OrderByDescending(w => w.Count(char.IsDigit)).FirstOrDefault();
        await AddQuotesAsync(items, number, cancellationToken).ConfigureAwait(false);
        await AddInvoicesAsync(items, number, cancellationToken).ConfigureAwait(false);
        return items.Where(i => PaletteMatcher.Score(i.Label, query) > 0).ToList();
    }

    private async Task AddCustomersAsync(List<PaletteItem> items, CancellationToken cancellationToken)
    {
        if (_customers is null && customerGateway.Value is { } customers)
        {
            OperationResult<IReadOnlyList<CustomerSummary>> result = await customers.ListAllAsync(cancellationToken).ConfigureAwait(false);
            _customers = result.Value ?? [];
        }

        string group = localizer["Palette.Group.Customers"];
        foreach (CustomerSummary customer in _customers ?? [])
        {
            items.Add(Go(group, Format("Palette.NewQuoteFor", customer.Name), $"quotes/new?customer={customer.Id}"));
            items.Add(Go(group, Format("Palette.NewInvoiceFor", customer.Name), $"invoices/new?customer={customer.Id}"));
        }
    }

    private async Task AddQuotesAsync(List<PaletteItem> items, string? search, CancellationToken cancellationToken)
    {
        if (quoteGateway.Value is not { } quotes)
        {
            return;
        }

        OperationResult<PagedResponse<QuoteSummary>> result =
            await quotes.ListAsync(new BillingListQuery(search, PageSize: 20), null, cancellationToken).ConfigureAwait(false);
        string group = localizer["Palette.Group.Documents"];
        foreach (QuoteSummary quote in result.Value?.Items ?? [])
        {
            string name = quote.Number ?? localizer["Billing.Draft"];
            items.Add(Go(group, Format("Palette.OpenQuote", name, quote.CustomerName), $"quotes/{quote.Id}"));
        }
    }

    private async Task AddInvoicesAsync(List<PaletteItem> items, string? search, CancellationToken cancellationToken)
    {
        if (invoiceGateway.Value is not { } invoices)
        {
            return;
        }

        OperationResult<PagedResponse<InvoiceSummary>> result =
            await invoices.ListAsync(new BillingListQuery(search, PageSize: 20), null, null, cancellationToken).ConfigureAwait(false);
        string group = localizer["Palette.Group.Documents"];
        foreach (InvoiceSummary invoice in result.Value?.Items ?? [])
        {
            string name = invoice.Number ?? localizer["Billing.Draft"];
            items.Add(Go(group, Format($"Palette.Open{invoice.Kind}", name, invoice.CustomerName), $"invoices/{invoice.Id}"));
            if (invoice.Kind != InvoiceKindDto.CreditNote && invoice.Status != InvoiceStatusDto.Draft)
            {
                items.Add(Go(group, Format("Palette.CreditNoteOn", name, invoice.CustomerName), $"invoices/{invoice.Id}?action=credit-note"));
            }
        }
    }

    private string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, localizer[key], args);

    private PaletteItem Go(string group, string label, string uri) =>
        new(group, label, () =>
        {
            navigation.NavigateTo(uri);
            return Task.CompletedTask;
        });
}
