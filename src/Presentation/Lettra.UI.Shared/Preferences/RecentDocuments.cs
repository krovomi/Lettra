using System.Globalization;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Preferences;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared.Components.Palette;
using Lettra.UI.Shared.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Preferences;

/// <summary>Une pièce ouverte récemment : ce que montre « Reprendre où j'en étais ».</summary>
public sealed record RecentDocument(UsageKindDto Kind, Guid Id, string Label, string Href);

/// <summary>
/// Les dernières factures et derniers devis ouverts par l'utilisateur dans le dossier. Le compte ne retient que leurs
/// identifiants : numéro et client sont relus dans le dossier, une fois par pièce et par session. Une pièce supprimée
/// ou devenue inaccessible disparaît de la liste.
/// </summary>
public sealed class RecentDocuments(IServiceProvider services, IStringLocalizer<SharedResource> localizer)
{
    public const int DefaultCount = 5;

    private readonly Dictionary<(UsageKindDto, Guid), RecentDocument?> _resolved = [];

    public async Task<IReadOnlyList<RecentDocument>> ListAsync(int count = DefaultCount, CancellationToken cancellationToken = default)
    {
        if (EntryPreferences.From(services) is not { DossierId: not null } entry)
        {
            return [];
        }

        await entry.Preferences.EnsureLoadedAsync().ConfigureAwait(false);
        Dictionary<Guid, DateTimeOffset> lastUsed = (entry.Preferences.Current.Usage.FirstOrDefault(u => u.DossierId == entry.DossierId)?.Entries ?? [])
            .Where(e => e.Kind is UsageKindDto.Invoice or UsageKindDto.Quote)
            .ToDictionary(e => e.Id, e => e.LastUsed);
        IEnumerable<(UsageKindDto Kind, Guid Id)> wanted = entry.Ranked(UsageKindDto.Invoice).Select(id => (Kind: UsageKindDto.Invoice, Id: id))
            .Concat(entry.Ranked(UsageKindDto.Quote).Select(id => (Kind: UsageKindDto.Quote, Id: id)))
            .OrderByDescending(d => lastUsed.GetValueOrDefault(d.Id));

        List<RecentDocument> found = [];
        foreach ((UsageKindDto kind, Guid id) in wanted)
        {
            if (found.Count >= count)
            {
                break;
            }

            if (await ResolveAsync(kind, id, cancellationToken).ConfigureAwait(false) is { } document)
            {
                found.Add(document);
            }
        }

        return found;
    }

    private async Task<RecentDocument?> ResolveAsync(UsageKindDto kind, Guid id, CancellationToken cancellationToken)
    {
        if (_resolved.TryGetValue((kind, id), out RecentDocument? known))
        {
            return known;
        }

        RecentDocument? document = kind == UsageKindDto.Invoice
            ? await InvoiceAsync(id, cancellationToken).ConfigureAwait(false)
            : await QuoteAsync(id, cancellationToken).ConfigureAwait(false);
        _resolved[(kind, id)] = document;
        return document;
    }

    private async Task<RecentDocument?> InvoiceAsync(Guid id, CancellationToken cancellationToken)
    {
        if (services.GetService<IInvoicesGateway>() is not { } invoices)
        {
            return null;
        }

        OperationResult<InvoiceResponse> read = await invoices.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return read.Value is { } invoice
            ? new(UsageKindDto.Invoice, id, Label("Recent.Invoice", invoice.Number, invoice.Customer.Name), $"invoices/{id}")
            : null;
    }

    private async Task<RecentDocument?> QuoteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (services.GetService<IQuotesGateway>() is not { } quotes)
        {
            return null;
        }

        OperationResult<QuoteResponse> read = await quotes.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return read.Value is { } quote
            ? new(UsageKindDto.Quote, id, Label("Recent.Quote", quote.Number, quote.Customer.Name), $"quotes/{id}")
            : null;
    }

    private string Label(string key, string? number, string customer) =>
        string.Format(CultureInfo.CurrentCulture, localizer[key], number ?? localizer["Recent.Draft"], customer);
}

/// <summary>Palette (Ctrl+K) : « Reprendre » les dernières pièces ouvertes, en tête quand rien n'est saisi.</summary>
public sealed class RecentDocumentsPaletteProvider(
    RecentDocuments recents,
    NavigationManager navigation,
    IStringLocalizer<SharedResource> localizer) : ICommandPaletteProvider
{
    public async Task<IReadOnlyList<PaletteItem>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        string group = localizer["Palette.Group.Resume"];
        IReadOnlyList<RecentDocument> documents = await recents.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return [.. documents
            .Select(d => new PaletteItem(group, string.Format(CultureInfo.CurrentCulture, localizer["Palette.Resume"], d.Label), () =>
            {
                navigation.NavigateTo(d.Href);
                return Task.CompletedTask;
            }))
            .Where(i => PaletteMatcher.Score(i.Label, query) > 0)];
    }
}
