using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Dossiers;
using Lettra.Bootstrap;
using Lettra.Contracts.Administration;
using Lettra.Contracts.Alerts;
using Lettra.Contracts.Common;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Sqlite.Local;
using Lettra.UI.Abstractions;
using Lettra.UI.Gateway.InProcess;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Poste isolé (mode 1) : le propriétaire du poste administre le cabinet créé au premier lancement
/// (<c>organisation.json</c>), celui auquel ses dossiers sont rattachés. Il y crée des dossiers et voit les alertes
/// de tout le cabinet ; un autre cabinet lui reste fermé.
/// </summary>
public sealed class LocalCabinetAuthorizationTests : IAsyncLifetime
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lettra-cabinet-").FullName;
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [LettraBootstrap.DeploymentModeKey] = "IsolatedDesktop",
            [LettraBootstrap.LocalDataDirectoryKey] = Path.Combine(_folder, "dossiers"),
            ["Lettra:LocalIdentity:StorePath"] = Path.Combine(_folder, "compte-local.json"),
            ["Lettra:LocalIdentity:Pbkdf2Iterations"] = "1000",
        }).Build();
        ServiceCollection collection = new();
        collection.AddLogging();
        collection.AddLettra(configuration);
        collection.AddSingleton<IKekStore, InMemoryKekStore>();
        collection.AddSingleton<IDossierSelection>(sp => new ActiveDossier(sp.GetRequiredService<LocalDossierRegistry>()));
        collection.AddInProcessGateways();
        _services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await _services.InitializeLettraAsync(configuration, CancellationToken.None);
        (await _services.GetRequiredService<ILocalAccounts>().CreateAsync("Camille Démo", "atelier des comptes 2026", default))
            .IsSuccess.Should().BeTrue();
    }

    private async Task<OrganisationId> LocalCabinetAsync() =>
        new((await _services.GetRequiredService<LocalDossierRegistry>().GetOrCreateOrganisationAsync(TimeProvider.System, default)).Id);

    private async Task<Result<DossierSummary>> CreateDossierAsync(OrganisationId cabinet)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
            .SendAsync(new CreateDossierCommand(cabinet, "Dossier Exemple"), default);
    }

    [Fact]
    public async Task Le_proprietaire_cree_un_dossier_dans_le_cabinet_du_poste()
    {
        OrganisationId cabinet = await LocalCabinetAsync();

        Result<DossierSummary> created = await CreateDossierAsync(cabinet);

        created.IsSuccess.Should().BeTrue(string.Join(", ", created.Errors.Select(e => e.Code)));
        (await _services.GetRequiredService<LocalDossierRegistry>().FindAsync(created.Value.Id, default))!
            .OrganisationId.Should().Be(cabinet.Value);
    }

    [Fact]
    public async Task Le_proprietaire_voit_les_alertes_de_tout_le_cabinet()
    {
        OperationResult<AlertCentreResponse> alerts = await _services.GetRequiredService<IAlertsGateway>()
            .ListAsync(cabinetWide: true, includeClosed: false, default);

        alerts.IsSuccess.Should().BeTrue(string.Join(", ", alerts.Errors.Select(e => e.Code)));
    }

    [Fact]
    public async Task Un_autre_cabinet_reste_ferme()
    {
        Result<DossierSummary> created = await CreateDossierAsync(new OrganisationId(Guid.CreateVersion7()));

        created.Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
    }

    [Fact]
    public async Task Le_proprietaire_voit_l_espace_d_administration_en_lecture_seule()
    {
        IAdministrationGateway gateway = _services.GetRequiredService<IAdministrationGateway>();
        OrganisationId cabinet = await LocalCabinetAsync();

        OperationResult<MyAccessResponse> access = await gateway.GetMyAccessAsync(null, default);
        access.Value!.CanAdminister.Should().BeTrue();
        access.Value.CabinetId.Should().Be(cabinet.Value);

        OperationResult<AdministrationOverview> overview = await gateway.GetOverviewAsync(cabinet.Value, default);

        overview.IsSuccess.Should().BeTrue(string.Join(", ", overview.Errors.Select(e => e.Code)));
        overview.Value!.Can.ReadOnly.Should().BeTrue();
        overview.Value.People.Should().ContainSingle().Which.Should().Match<PersonSummary>(p => p.DisplayName == "Camille Démo" && p.IsMe);
        overview.Value.People[0].Assignments.Select(a => a.Role).Should().BeEquivalentTo("manager", "administrateur-cabinet");
        (await gateway.InviteAsync(cabinet.Value, new InvitePersonRequest { DisplayName = "Léa", Email = "lea@exemple.fr" }, default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Rights.NotSupported);
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    private sealed class ActiveDossier(LocalDossierRegistry registry) : IDossierSelection
    {
        public Guid? CurrentDossierId => registry.ActiveDossierId;
    }
}
