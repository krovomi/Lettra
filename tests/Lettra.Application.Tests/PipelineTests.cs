using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Customers;
using Lettra.Application.Features.Dossiers;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lettra.Application.Tests;

public sealed class PipelineTests : IAsyncLifetime
{
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());

    private readonly FakeTenantContext _tenant = new()
    {
        Descriptor = new TenantDescriptor(Dossier, new OrganisationId(Guid.CreateVersion7()), DatabaseProvider.Sqlite, "config:test"),
    };

    private readonly FakeUser _user = new();
    private readonly FakePermissions _permissions = new();
    private readonly InMemoryCustomers _customers = new();
    private readonly RecordingDossierProvisioner _provisioner = new();
    private readonly CapturingLoggerProvider _logs = new();
    private ServiceProvider _root = null!;
    private AsyncServiceScope _scope;

    public Task InitializeAsync()
    {
        ServiceCollection services = new();
        services.AddLogging(b => b.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace));
        services.AddApplication();
        services.AddSingleton<ITenantContext>(_tenant);
        services.AddSingleton<ICurrentUser>(_user);
        services.AddSingleton<IPermissionService>(_permissions);
        services.AddSingleton<ICustomerRepository>(_customers);
        services.AddSingleton<ICustomerReadStore>(_customers);
        services.AddSingleton<IUnitOfWork>(_customers);
        services.AddPartiesAndCatalogFakes(new InMemoryPartiesAndCatalog(), new FakeDirectories());
        services.AddIntegrityFakes(new InMemoryIntegrity(Dossier));
        services.AddSingleton<IDossierProvisioner>(_provisioner);
        _root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _scope = _root.CreateAsyncScope();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _root.DisposeAsync();
    }

    private ICommandDispatcher Commands => _scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

    private IQueryDispatcher Queries => _scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();

    private static Customer.Draft ValidDraft() => new(
        CustomerKind.Company, "Boulangerie Martin", null, "73282932000074", "FR44732829320", "compta@martin.fr",
        "1 rue de la Paix", null, "75002", "Paris", "FR");

    [Fact]
    public async Task Creer_un_client_le_persiste_et_le_rend_lisible()
    {
        Result<CustomerId> created = await Commands.SendAsync(new CreateCustomerCommand(Dossier, ValidDraft()), default);

        created.IsSuccess.Should().BeTrue();
        Result<CustomerResponse> read = await Queries.QueryAsync(new GetCustomerQuery(Dossier, created.Value), default);
        read.Value.Siren.Should().Be("732829320");
        read.Value.Kind.Should().Be(CustomerKindDto.Company);
    }

    [Fact]
    public async Task Erreurs_metier_renvoyees_sans_rien_ecrire()
    {
        Result<CustomerId> created = await Commands.SendAsync(
            new CreateCustomerCommand(Dossier, ValidDraft() with { Siret = null }), default);

        created.Errors.Should().ContainSingle().Which.Code.Should().Be(CustomerErrors.SirenRequiredCode);
        _customers.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Sans_la_permission_la_commande_est_refusee_avant_le_handler()
    {
        _permissions.Granted.Remove(Permission.TiersCreer);

        Result<CustomerId> created = await Commands.SendAsync(new CreateCustomerCommand(Dossier, ValidDraft()), default);

        created.Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
        _customers.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Creer_un_dossier_se_verifie_sur_le_cabinet_et_nettoie_le_nom()
    {
        OrganisationId cabinet = new(Guid.CreateVersion7());

        Result<DossierSummary> created = await Commands.SendAsync(new CreateDossierCommand(cabinet, "  Garage Martin  "), default);

        created.IsSuccess.Should().BeTrue();
        created.Value.Name.Should().Be("Garage Martin");
        _provisioner.Created.Should().Equal((cabinet, "Garage Martin"));
        _permissions.Asked.Should().Contain(AuthorizationScope.ForCabinet(cabinet));
    }

    [Fact]
    public async Task Sans_DossiersCreer_aucun_dossier_n_est_cree()
    {
        _permissions.Granted.Remove(Permission.DossiersCreer);

        Result<DossierSummary> created = await Commands.SendAsync(new CreateDossierCommand(new OrganisationId(Guid.CreateVersion7()), "Garage"), default);

        created.Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
        _provisioner.Created.Should().BeEmpty();
    }

    [Theory]
    [InlineData("   ", DossierErrors.NameRequiredCode)]
    [InlineData(null, DossierErrors.NameTooLongCode)]
    public async Task Un_nom_invalide_ne_cree_rien(string? name, string code)
    {
        name ??= new string('x', CreateDossierRequest.MaxNameLength + 1);

        Result<DossierSummary> created = await Commands.SendAsync(new CreateDossierCommand(new OrganisationId(Guid.CreateVersion7()), name), default);

        created.Errors.Should().ContainSingle().Which.Code.Should().Be(code);
        _provisioner.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Utilisateur_non_authentifie_refuse()
    {
        _user.IsAuthenticated = false;

        Result<PagedResponse<CustomerSummary>> result = await Queries.QueryAsync(new ListCustomersQuery(Dossier), default);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.UnauthenticatedCode);
    }

    [Fact]
    public async Task Une_commande_visant_un_autre_dossier_est_refusee()
    {
        DossierId other = new(Guid.CreateVersion7());

        Result<CustomerId> created = await Commands.SendAsync(new CreateCustomerCommand(other, ValidDraft()), default);

        created.Errors.Should().ContainSingle().Which.Code.Should().Be(TenancyErrors.DossierMismatchCode);
        _customers.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Sans_dossier_ouvert_rien_ne_s_execute()
    {
        _tenant.Descriptor = null;

        Result<CustomerId> created = await Commands.SendAsync(new CreateCustomerCommand(Dossier, ValidDraft()), default);

        created.Errors.Should().ContainSingle().Which.Code.Should().Be(TenancyErrors.DossierRequiredCode);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 500)]
    public async Task Pagination_validee(int page, int pageSize)
    {
        Result<PagedResponse<CustomerSummary>> result = await Queries.QueryAsync(new ListCustomersQuery(Dossier, page, pageSize), default);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Validation.Invalid);
    }

    [Fact]
    public async Task Aucune_donnee_de_dossier_dans_les_journaux()
    {
        await Commands.SendAsync(new CreateCustomerCommand(Dossier, ValidDraft()), default);
        await Commands.SendAsync(new CreateCustomerCommand(Dossier, ValidDraft() with { Siret = "123" }), default);
        await Queries.QueryAsync(new ListCustomersQuery(Dossier), default);

        _logs.Entries.Should().NotBeEmpty();
        string[] secrets = ["Boulangerie", "Martin", "732829320", "73282932000074", "FR44732829320", "compta@", "rue de la Paix", "75002", "Paris"];
        foreach (string entry in _logs.Entries)
        {
            entry.Should().NotContainAny(secrets);
        }
    }
}
