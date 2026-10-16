using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Shared.Onboarding;

/// <summary>Une étape de la liste de démarrage du tableau de bord.</summary>
public sealed record StarterStep(string Id, string Href, bool Done);

/// <summary>
/// Liste de démarrage (vague 5, lane F) : fiche entreprise, premier client, premier devis, devis transformé en facture,
/// premier export FEC. Chaque étape se coche toute seule d'après les données du dossier ; seul l'export FEC, qui ne
/// laisse pas de trace côté serveur, est noté sur l'appareil quand il réussit. La liste masquée l'est pour le dossier,
/// sur cet appareil.
/// </summary>
public sealed class StarterProgress(DevicePreferences preferences, IServiceProvider services)
{
    public const string Company = "company";
    public const string Customer = "customer";
    public const string Quote = "quote";
    public const string Invoice = "invoice";
    public const string Fec = "fec";

    private const string Hidden = "hidden";

    private HashSet<string>? _flags;

    /// <summary>Les cinq étapes, dans l'ordre, chacune cochée ou non.</summary>
    public async Task<IReadOnlyList<StarterStep>> ReadAsync(CancellationToken cancellationToken)
    {
        Task<bool> company = CompanyDoneAsync(cancellationToken);
        Task<bool> customer = CountAsync<ICustomersGateway, CustomerSummary>(g => g.ListAsync(1, 1, cancellationToken));
        Task<bool> quote = CountAsync<IQuotesGateway, QuoteSummary>(g => g.ListAsync(new BillingListQuery(PageSize: 1), null, cancellationToken));
        Task<bool> invoiced = CountAsync<IQuotesGateway, QuoteSummary>(g => g.ListAsync(new BillingListQuery(PageSize: 1), QuoteStatusDto.Invoiced, cancellationToken));
        Task<HashSet<string>> flags = LoadAsync();
        await Task.WhenAll(company, customer, quote, invoiced, flags).ConfigureAwait(false);

        return
        [
            new(Company, "settings/company", await company.ConfigureAwait(false)),
            new(Customer, "customers", await customer.ConfigureAwait(false)),
            new(Quote, "quotes/new", await quote.ConfigureAwait(false)),
            new(Invoice, "quotes", await invoiced.ConfigureAwait(false)),
            new(Fec, "exports/fec", (await flags.ConfigureAwait(false)).Contains(Fec)),
        ];
    }

    public async Task<bool> IsHiddenAsync() => (await LoadAsync().ConfigureAwait(false)).Contains(Hidden);

    public Task HideAsync() => AddFlagAsync(Hidden);

    /// <summary>Appelé par l'écran FEC après un export réussi.</summary>
    public Task MarkFecExportedAsync() => AddFlagAsync(Fec);

    private async Task AddFlagAsync(string flag)
    {
        HashSet<string> flags = await LoadAsync().ConfigureAwait(false);
        if (flags.Add(flag))
        {
            await preferences.WriteAsync(StorageKey, string.Join(';', flags.Order(StringComparer.Ordinal))).ConfigureAwait(false);
        }
    }

    private string StorageKey => "lettra.starter." + (services.GetService<IDossierSelection>()?.CurrentDossierId?.ToString("N") ?? "local");

    private async Task<HashSet<string>> LoadAsync()
    {
        if (_flags is not null)
        {
            return _flags;
        }

        string? stored = await preferences.ReadAsync(StorageKey).ConfigureAwait(false);
        return _flags ??= [.. (stored ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries)];
    }

    private async Task<bool> CompanyDoneAsync(CancellationToken cancellationToken)
    {
        if (services.GetService<IBillingSettingsGateway>() is not { } settings)
        {
            return false;
        }

        OperationResult<CompanyProfileResponse> profile = await settings.GetCompanyProfileAsync(cancellationToken).ConfigureAwait(false);
        return profile.Value is { } company && !string.IsNullOrWhiteSpace(company.Name) && !string.IsNullOrWhiteSpace(company.Siren);
    }

    private async Task<bool> CountAsync<TGateway, TItem>(Func<TGateway, Task<OperationResult<PagedResponse<TItem>>>> list)
        where TGateway : class
    {
        if (services.GetService<TGateway>() is not { } gateway)
        {
            return false;
        }

        OperationResult<PagedResponse<TItem>> result = await list(gateway).ConfigureAwait(false);
        return result.Value is { TotalCount: > 0 };
    }
}
