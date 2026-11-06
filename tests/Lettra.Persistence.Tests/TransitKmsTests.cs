using System.Net.Http.Json;
using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Catalog;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Catalog;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Sqlite;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lettra.Persistence.Tests;

/// <summary>
/// KEK des cabinets tenues par le moteur Transit d'OpenBao (mode 5) : créée une fois, gardée enveloppée dans le catalogue,
/// la même après une rotation de la clé racine, et perdue pour de bon quand la clé racine est détruite.
/// </summary>
public sealed class TransitKmsTests : IAsyncLifetime, IDisposable
{
    private const string Token = "essai-lettra";
    private readonly string _folder = Directory.CreateTempSubdirectory("lettra-kms-").FullName;
    private IContainer? _openBao;

    public async Task InitializeAsync()
    {
        if (!Docker.Available)
        {
            return;
        }

        _openBao = new ContainerBuilder("openbao/openbao:2.4.1")
            .WithCommand("server", "-dev", $"-dev-root-token-id={Token}", "-dev-listen-address=0.0.0.0:8200")
            .WithPortBinding(8200, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8200).ForPath("/v1/sys/health")))
            .Build();
        await _openBao.StartAsync();
        using HttpClient http = new() { BaseAddress = Address };
        http.DefaultRequestHeaders.Add("X-Vault-Token", Token);
        (await http.PostAsJsonAsync("v1/sys/mounts/transit", new { type = "transit" })).EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync()
    {
        if (_openBao is not null)
        {
            await _openBao.DisposeAsync();
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    private Uri Address => new($"http://{_openBao!.Hostname}:{_openBao.GetMappedPublicPort(8200)}/");

    [SkippableFact]
    public async Task Cle_du_cabinet_creee_par_le_KMS_tournee_puis_detruite()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using CatalogHarness harness = Harness("rotation");
        IServiceProvider services = harness.Services;
        await services.InitializeCatalogAsync(migrate: true, default);
        IKekStore store = services.GetRequiredService<IKekStore>();
        store.Should().BeOfType<TransitKekStore>();
        OrganisationId cabinet = CatalogHarness.Organisation;

        byte[] kek = await store.GetOrCreateAsync(cabinet, default);
        kek.Should().HaveCount(32);
        string wrapped = (await WrappedKekAsync(services, cabinet))!;
        wrapped.Should().StartWith("vault:v1:").And.NotContain(Convert.ToBase64String(kek));
        (await store.GetOrCreateAsync(cabinet, default)).Should().Equal(kek, "la KEK n'est tirée qu'une fois");
        (await Fresh(services).FindAsync(cabinet, default)).Should().Equal(kek, "une autre instance l'ouvre par le KMS");

        DossierId dossier = (await services.GetRequiredService<IDossierProvisioner>().CreateAsync(cabinet, "Dossier KMS", default)).Value;
        ICabinetKeyVault vault = services.GetRequiredService<ICabinetKeyVault>();
        (await vault.RewrapAllAsync(default)).Should().Be(0, "l'enveloppe est déjà à la dernière version");
        await vault.RotateAsync(cabinet, default);
        (await WrappedKekAsync(services, cabinet)).Should().StartWith("vault:v2:");
        (await Fresh(services).FindAsync(cabinet, default)).Should().Equal(kek, "la rotation ne change ni la KEK ni les DEK");
        await OpenAsync(services, dossier, Fresh(services));

        await vault.DestroyAsync(cabinet, default);
        TransitKekStore after = Fresh(services);
        (await after.FindAsync(cabinet, default)).Should().BeNull();
        Func<Task> recreate = async () => await after.GetOrCreateAsync(cabinet, default);
        await recreate.Should().ThrowAsync<CryptographicException>().WithMessage("*détruite*");
        Func<Task> open = () => OpenAsync(services, dossier, after);
        await open.Should().ThrowAsync<CryptographicException>();
    }

    [SkippableFact]
    public async Task KMS_injoignable_signale_et_non_confondu_avec_une_cle_detruite()
    {
        Skip.IfNot(Docker.Available, "Docker absent");
        await using CatalogHarness harness = Harness("panne");
        IServiceProvider services = harness.Services;
        await services.InitializeCatalogAsync(migrate: true, default);
        _ = await services.GetRequiredService<IKekStore>().GetOrCreateAsync(CatalogHarness.Organisation, default);

        TransitKekStore wrongToken = new(
            services.GetRequiredService<IHttpClientFactory>(),
            Options.Create(new KmsOptions { Provider = KmsProvider.Transit, Transit = { Address = Address, Token = "jeton-revoque" } }),
            services.GetRequiredService<IDbContextFactory<CatalogDbContext>>());
        Func<Task> find = async () => await wrongToken.FindAsync(CatalogHarness.Organisation, default);
        await find.Should().ThrowAsync<HttpRequestException>();
    }

    private CatalogHarness Harness(string name) => new(
        s => s.AddSqliteProvider(),
        b => SqliteConfigurator.ConfigureCatalog(b, $"Data Source={Path.Combine(_folder, name)}.db;Pooling=False"),
        new Dictionary<string, string?> { ["Provider"] = "Sqlite", ["Directory"] = Path.Combine(_folder, $"srv-{name}") },
        new Dictionary<string, string?>
        {
            ["Lettra:Kms:Provider"] = "Transit",
            ["Lettra:Kms:Transit:Address"] = Address.ToString(),
            ["Lettra:Kms:Transit:Token"] = Token,
        });

    /// <summary>Une autre instance de l'API ou du Worker : son cache est vide, elle demande au KMS.</summary>
    private static TransitKekStore Fresh(IServiceProvider services) => new(
        services.GetRequiredService<IHttpClientFactory>(),
        services.GetRequiredService<IOptions<KmsOptions>>(),
        services.GetRequiredService<IDbContextFactory<CatalogDbContext>>());

    private static async Task<string?> WrappedKekAsync(IServiceProvider services, OrganisationId cabinet)
    {
        await using CatalogDbContext db = await services.GetRequiredService<IDbContextFactory<CatalogDbContext>>().CreateDbContextAsync();
        return await db.Organisations.Where(o => o.Id == cabinet.Value).Select(o => o.WrappedKek).SingleAsync();
    }

    /// <summary>Ouvre le dossier avec les clés d'une instance donnée : DEK lue au catalogue, ouverte par sa KEK.</summary>
    private static async Task OpenAsync(IServiceProvider services, DossierId dossier, IKekStore keks)
    {
        TenantDescriptor descriptor = (await services.GetRequiredService<ITenantCatalog>().FindAsync(dossier, default))!;
        using CatalogDossierFieldKeys fieldKeys = new(services.GetRequiredService<IDbContextFactory<CatalogDbContext>>(), new AesGcmDossierKeyring(keks));
        byte[] dek = (await fieldKeys.FindAsync(descriptor, default))!;
        dek.Should().HaveCount(32);
        await using LettraDbContext db = await services.GetRequiredService<IDossierDbContextFactory>().CreateAsync(descriptor, verifyStamp: true, default);
        (await db.Customers.CountAsync()).Should().Be(0);
    }
}
