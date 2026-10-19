using System.Net;
using System.Net.Http.Headers;
using Lettra.Application.Abstractions.Identity;
using Lettra.Bootstrap;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Sqlite.Local;
using Lettra.UI.Abstractions;
using Lettra.UI.Gateway.Http;
using Lettra.UI.Gateway.InProcess;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Lettra.Api.Tests;

/// <summary>
/// ADR-0005 : la même interface d'UI, par HTTP (WASM, contre l'API de test) ou en direct (poste isolé, SQLite chiffrée).
/// Un banc par test : le dossier du poste est neuf à chaque fois, celui de l'API est partagé par la classe.
/// </summary>
internal sealed class GatewayHarness(LettraApiFactory factory) : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lettra-maui-").FullName;

    public static TheoryData<string> Transports => ["http", "in-process"];

    /// <summary>Gateways HTTP du navigateur, authentifiées comme <paramref name="user"/> (le manager par défaut).</summary>
    public ServiceProvider HttpGateway(Guid dossier, Guid? user = null)
    {
        ServiceCollection services = new();
        services.AddSingleton<IDossierSelection>(new FixedDossierSelection(dossier));
        services.AddHttpGateways(new Uri("http://localhost/"));
        services.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(b =>
        {
            b.PrimaryHandler = factory.Server.CreateHandler();
            b.AdditionalHandlers.Insert(0, new BearerHandler(LettraApiFactory.TokenFor(user ?? LettraApiFactory.ManagerId)));
        }));
        return services.BuildServiceProvider();
    }

    /// <summary>Le poste isolé (mode 1) : registre local, premier dossier créé au démarrage, base SQLite chiffrée.</summary>
    public ServiceProvider InProcessGateway(Guid? dossier)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [LettraBootstrap.DeploymentModeKey] = "IsolatedDesktop",
            [LettraBootstrap.LocalDataDirectoryKey] = _folder,
            ["Lettra:LocalIdentity:StorePath"] = Path.Combine(_folder, "compte-local.json"),
            ["Lettra:LocalIdentity:Pbkdf2Iterations"] = "1000",
            // Comme l'API de test : la plateforme agréée simulée (par défaut, aucune plateforme et tout envoi échoue).
            ["Lettra:EInvoicing:Platform"] = "Simulated",
        }).Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddLettra(configuration);
        services.AddSingleton<IKekStore, InMemoryKekStore>();
        services.AddSingleton<IDossierSelection>(sp => new FixedDossierSelection(dossier ?? sp.GetRequiredService<LocalDossierRegistry>().ActiveDossierId));
        services.AddInProcessGateways();
        ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        // Le poste isolé n'ouvre rien avant le déverrouillage : on crée le compte local, ce qui ouvre la session.
        ILocalAccounts accounts = provider.GetRequiredService<ILocalAccounts>();
        if (!accounts.GetStatusAsync(default).AsTask().GetAwaiter().GetResult().Exists)
        {
            accounts.CreateAsync("Poste de test", "phrase secrète de test", default).AsTask().GetAwaiter().GetResult().IsSuccess.Should().BeTrue();
        }
        else
        {
            accounts.UnlockAsync("phrase secrète de test", default).AsTask().GetAwaiter().GetResult().IsSuccess.Should().BeTrue();
        }

        provider.InitializeLettraAsync(configuration, CancellationToken.None).GetAwaiter().GetResult();
        return provider;
    }

    public ServiceProvider Gateway(string transport, Guid? dossier = null) =>
        transport == "http"
            ? HttpGateway(dossier ?? LettraApiFactory.DossierId)
            : InProcessGateway(dossier);

    /// <summary>Gateways HTTP dont chaque requête répond par <paramref name="respond"/> (serveur simulé, sans API).</summary>
    public static ServiceProvider Answering(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        ServiceCollection services = new();
        services.AddSingleton<IDossierSelection>(new FixedDossierSelection(LettraApiFactory.DossierId));
        services.AddHttpGateways(new Uri("http://lettra.test/"));
        services.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = new Responder(respond)));
        return services.BuildServiceProvider();
    }

    /// <summary>Gateways HTTP vers un serveur injoignable : chaque envoi lève <see cref="HttpRequestException"/>.</summary>
    public static ServiceProvider Unreachable() =>
        Answering(_ => throw new HttpRequestException(HttpRequestError.ConnectionError, "Serveur injoignable (test)."));

    /// <summary>Réponse d'erreur au corps illisible (page HTML d'un proxy, par exemple).</summary>
    public static HttpResponseMessage HtmlError(HttpStatusCode status) =>
        new(status) { Content = new StringContent("<html><body>Bad gateway</body></html>", System.Text.Encoding.UTF8, "text/html") };

    public static SaveCompanyProfileRequest CompanyProfile(VatDeclarationRegimeDto? regime = null) => new()
    {
        Name = "Conseil Lettra",
        LegalForm = "SAS",
        ShareCapital = 10_000m,
        Siren = "552100554",
        Rcs = "RCS Paris 552 100 554",
        VatNumber = "FR96552100554",
        AddressLine1 = "10 avenue de l'Opéra",
        PostalCode = "75001",
        City = "Paris",
        LatePenaltyRate = 10m,
        EarlyPaymentDiscount = "Pas d'escompte pour paiement anticipé",
        VatDeclarationRegime = regime,
    };

    public static async Task SaveCompanyProfileAsync(IServiceProvider services, VatDeclarationRegimeDto? regime = null) =>
        (await services.GetRequiredService<IBillingSettingsGateway>().SaveCompanyProfileAsync(CompanyProfile(regime), null, default))
            .IsSuccess.Should().BeTrue();

    public static DocumentLineRequest Conseil(decimal quantity = 1m) =>
        new() { Designation = "Conseil", Quantity = quantity, UnitPriceExclTax = 100m, UnitCode = "HUR", Kind = CatalogItemKindDto.Service };

    /// <summary>Un client, la fiche entreprise et une facture de 100 € HT (120 € TTC) validée.</summary>
    public static async Task<InvoiceResponse> ValidatedInvoiceAsync(IServiceProvider services)
    {
        IInvoicesGateway invoices = services.GetRequiredService<IInvoicesGateway>();
        Guid customer = (await services.GetRequiredService<ICustomersGateway>().CreateAsync(CustomerApiTests.Martin(), default)).Value!.Id;
        await SaveCompanyProfileAsync(services);
        InvoiceResponse draft = (await invoices.CreateAsync(new SaveInvoiceRequest { CustomerId = customer, Lines = [Conseil()] }, default)).Value!;
        OperationResult<InvoiceResponse> validated = await invoices.ValidateAsync(draft.Id, draft.Version, default);
        validated.IsSuccess.Should().BeTrue(string.Join(", ", validated.Errors.Select(e => e.Code)));
        return validated.Value!;
    }

    public static string Codes<T>(OperationResult<T> result) => string.Join(", ", result.Errors.Select(e => e.Code));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    private sealed class BearerHandler(string token) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class Responder(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
