using System.Globalization;
using System.Text;
using Lettra.Application.Abstractions.External;
using Lettra.Contracts.Directory;
using Lettra.Domain.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.DemoData;

/// <summary>
/// Annuaire SIRENE de la démonstration : les entreprises fictives du jeu « Cabinet Démo » (les trois entreprises des
/// dossiers, leurs clients et leurs fournisseurs) répondent comme dans le vrai annuaire, tirées du même scénario que le
/// chargement. Les clients qui ne paient plus (<see cref="DemoPayer.Defaulting"/>) y figurent comme cessés : le
/// contrôle des clients a quelque chose à montrer. Toute autre recherche part vers l'annuaire réel.
/// </summary>
public static class DemoCompanyDirectoryRegistration
{
    /// <summary>
    /// Place l'annuaire de démonstration devant l'annuaire réel, seulement si le jeu est demandé
    /// (<c>Lettra:Demo:Seed=true</c>) et hors environnement <c>Production</c>. À appeler après <c>AddLettra</c>.
    /// </summary>
    public static IServiceCollection AddDemoCompanyDirectory(this IServiceCollection services, IConfiguration configuration, string environmentName)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!LettraDemoData.IsAvailable(configuration, environmentName))
        {
            return services;
        }

        DemoDataOptions options = configuration.GetSection(DemoDataOptions.SectionName).Get<DemoDataOptions>() ?? new DemoDataOptions();
        ServiceDescriptor? real = services.LastOrDefault(d => d.ServiceType == typeof(ICompanyDirectory));
        if (real is null)
        {
            return services;
        }

        services.Remove(real);
        Lazy<DemoDirectoryData> data = new(() => DemoDirectoryData.Build(options));
        services.Add(new ServiceDescriptor(typeof(ICompanyDirectory),
            sp => new DemoCompanyDirectory(data.Value, Resolve(real, sp)), real.Lifetime));
        return services;
    }

    private static ICompanyDirectory Resolve(ServiceDescriptor descriptor, IServiceProvider services) => descriptor switch
    {
        { ImplementationInstance: ICompanyDirectory instance } => instance,
        { ImplementationFactory: { } factory } => (ICompanyDirectory)factory(services),
        { ImplementationType: { } type } => (ICompanyDirectory)ActivatorUtilities.CreateInstance(services, type),
        _ => throw new InvalidOperationException("Annuaire SIRENE non résolu."),
    };
}

/// <summary>Les fiches de l'annuaire de démonstration, par SIREN.</summary>
internal sealed class DemoDirectoryData(IReadOnlyDictionary<string, CompanySearchResult> companies)
{
    public IReadOnlyDictionary<string, CompanySearchResult> Companies { get; } = companies;

    public static DemoDirectoryData Build(DemoDataOptions options)
    {
        // Les fiches ne dépendent pas de la date : un jour fixe donne les mêmes entreprises que le chargement.
        DateOnly today = new(2026, 1, 15);
        int months = Math.Clamp(options.Months, 2, 36);
        Dictionary<string, CompanySearchResult> companies = new(StringComparer.Ordinal);
        foreach (DemoCompany company in Enum.GetValues<DemoBusiness>().Select(DemoCompanies.For).Concat(DemoCompanies.Peers))
        {
            DemoScenario scenario = DemoScenarioGenerator.Generate(company, options.RandomSeed, today, months);
            Add(companies, scenario.Profile.Siren, scenario.Profile.Siret, scenario.Profile.Name, scenario.Profile.AddressLine1,
                scenario.Profile.PostalCode, scenario.Profile.City, active: true, scenario.Profile.ActivityCode);
            foreach (DemoCustomer customer in scenario.Customers.Where(c => c.IsCompany()))
            {
                Add(companies, customer.Request.Siren, customer.Request.Siret, customer.Request.Name, customer.Request.AddressLine1,
                    customer.Request.PostalCode, customer.Request.City, active: customer.Payer != DemoPayer.Defaulting);
            }

            foreach (DemoSupplier supplier in scenario.Suppliers)
            {
                Add(companies, supplier.Request.Siren, supplier.Request.Siret, supplier.Request.Name, supplier.Request.AddressLine1,
                    supplier.Request.PostalCode, supplier.Request.City, active: true);
            }
        }

        return new DemoDirectoryData(companies);
    }

    private static void Add(Dictionary<string, CompanySearchResult> companies, string? siren, string? siret, string name,
        string? address, string? postalCode, string? city, bool active, string? activityCode = null)
    {
        if (siren is null)
        {
            return;
        }

        companies[siren] = new CompanySearchResult(siren, siret, name.ToUpper(CultureInfo.GetCultureInfo("fr-FR")), address, null,
            postalCode, city, "FR", FrenchIdentifiers.VatNumber(siren), activityCode, active);
    }
}

/// <summary>Répond pour les entreprises fictives de la démo, puis complète avec l'annuaire réel.</summary>
internal sealed class DemoCompanyDirectory(DemoDirectoryData data, ICompanyDirectory real) : ICompanyDirectory
{
    public async Task<Result<CompanySearchResponse>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        List<CompanySearchResult> local = [.. Find(query).Take(maxResults)];
        if (local.Count == maxResults)
        {
            return new CompanySearchResponse(local, local.Count);
        }

        Result<CompanySearchResponse> remote = await real.SearchAsync(query, maxResults - local.Count, cancellationToken).ConfigureAwait(false);
        if (remote.IsFailure)
        {
            // Hors ligne : les entreprises de la démo restent trouvables.
            return local.Count > 0 ? new CompanySearchResponse(local, local.Count) : remote;
        }

        List<CompanySearchResult> merged = [.. local, .. remote.Value.Items.Where(r => !data.Companies.ContainsKey(r.Siren))];
        return new CompanySearchResponse(merged, local.Count + remote.Value.TotalCount);
    }

    private IEnumerable<CompanySearchResult> Find(string query)
    {
        string digits = string.Concat(query.Where(char.IsAsciiDigit));
        if (digits.Length is 9 or 14 && digits.Length == query.Count(c => !char.IsWhiteSpace(c)))
        {
            return data.Companies.TryGetValue(digits[..9], out CompanySearchResult? company) ? [company] : [];
        }

        string wanted = Fold(query);
        return wanted.Length == 0 ? [] : data.Companies.Values.Where(c => Fold(c.Name).Contains(wanted, StringComparison.Ordinal)).OrderBy(c => c.Name, StringComparer.Ordinal);
    }

    private static string Fold(string text)
    {
        StringBuilder folded = new(text.Length);
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                folded.Append(char.ToUpperInvariant(c));
            }
        }

        return folded.ToString().Trim();
    }
}
