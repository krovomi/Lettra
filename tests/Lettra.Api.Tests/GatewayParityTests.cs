using System.Net.Http.Headers;
using Lettra.Application.Abstractions.Identity;
using Lettra.Bootstrap;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
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
/// ADR-0005 : la même UI appelle la même interface, par HTTP (WASM) ou en direct (MAUI hors ligne).
/// Les deux chemins doivent produire le même résultat et les mêmes codes d'erreur.
/// </summary>
public sealed class GatewayParityTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lettra-maui-").FullName;

    private ServiceProvider HttpGateway(Guid dossier)
    {
        ServiceCollection services = new();
        services.AddSingleton<IDossierSelection>(new FixedDossierSelection(dossier));
        services.AddHttpGateways(new Uri("http://localhost/"));
        services.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(b =>
        {
            b.PrimaryHandler = factory.Server.CreateHandler();
            b.AdditionalHandlers.Insert(0, new BearerHandler(LettraApiFactory.TokenFor(LettraApiFactory.ManagerId)));
        }));
        return services.BuildServiceProvider();
    }

    /// <summary>Le poste isolé (mode 1) : registre local, premier dossier créé au démarrage, base SQLite chiffrée.</summary>
    private ServiceProvider InProcessGateway(Guid? dossier)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [LettraBootstrap.DeploymentModeKey] = "IsolatedDesktop",
            [LettraBootstrap.LocalDataDirectoryKey] = _folder,
            ["Lettra:LocalIdentity:StorePath"] = Path.Combine(_folder, "compte-local.json"),
            ["Lettra:LocalIdentity:Pbkdf2Iterations"] = "1000",
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

    public static TheoryData<string> Transports => ["http", "in-process"];

    private ServiceProvider Gateway(string transport, Guid? dossier = null) =>
        transport == "http"
            ? HttpGateway(dossier ?? LettraApiFactory.DossierId)
            : InProcessGateway(dossier);

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Creer_puis_lister(string transport)
    {
        await using ServiceProvider services = Gateway(transport);
        ICustomersGateway gateway = services.GetRequiredService<ICustomersGateway>();

        OperationResult<CustomerResponse> created = await gateway.CreateAsync(CustomerApiTests.Martin(), default);
        OperationResult<PagedResponse<CustomerSummary>> list = await gateway.ListAsync(1, 50, default);

        created.IsSuccess.Should().BeTrue();
        created.Value!.Siren.Should().Be("732829320");
        list.Value!.Items.Should().Contain(c => c.Id == created.Value.Id);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Memes_codes_d_erreur_par_champ(string transport)
    {
        await using ServiceProvider services = Gateway(transport);
        CreateCustomerRequest request = CustomerApiTests.Martin();
        request.Siret = "73282932000075";
        request.PostalCode = "7500";

        OperationResult<CustomerResponse> result = await services.GetRequiredService<ICustomersGateway>().CreateAsync(request, default);

        result.Errors.Should().BeEquivalentTo(new[]
        {
            new ErrorDetail(ErrorCodes.SharedKernel.SiretInvalid, nameof(CreateCustomerRequest.Siret)),
            new ErrorDetail(ErrorCodes.SharedKernel.PostalCodeInvalid, nameof(CreateCustomerRequest.PostalCode)),
        });
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Dossier_inconnu_meme_refus(string transport)
    {
        await using ServiceProvider services = Gateway(transport, dossier: Guid.CreateVersion7());

        OperationResult<PagedResponse<CustomerSummary>> result = await services.GetRequiredService<ICustomersGateway>().ListAsync(1, 10, default);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Tenancy.DossierNotFound);
    }

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
}
