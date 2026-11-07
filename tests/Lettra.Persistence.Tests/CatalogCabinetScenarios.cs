using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Authorization;
using Lettra.Contracts.Platform;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Catalog;
using Lettra.Infrastructure.Persistence.Catalog;
using Lettra.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Arrivée puis départ d'un cabinet (plan du mode 5, étape 2) : inscrit avec sa clé, il part après le délai de
/// rétractation sans rien laisser (bases, clé, comptes, registre), sauf la trace de son départ. Joué sur chaque provider.
/// </summary>
internal static class CatalogCabinetScenarios
{
    public static readonly DateTimeOffset Start = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    /// <summary>L'horloge, un KMS et un fournisseur d'identité d'essai qui notent ce qu'on leur demande.</summary>
    public static Action<IServiceCollection> Platform(ManualClock clock, Vault vault, Identity identity) => s =>
    {
        s.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
        s.Replace(ServiceDescriptor.Singleton<IKekStore, InMemoryKekStore>());
        s.AddSingleton<ICabinetKeyVault>(vault);
        s.Replace(ServiceDescriptor.Singleton<IIdentityProvisioning>(identity));
    };

    /// <param name="harness">Le catalogue, avec un serveur de bases.</param>
    /// <param name="clock">L'horloge du catalogue, avancée pour franchir le délai de rétractation.</param>
    /// <param name="vault">Le KMS d'essai, qui note les clés détruites.</param>
    /// <param name="identity">Le fournisseur d'identité d'essai, qui note les organisations supprimées.</param>
    /// <param name="databaseExists">Dit si la base d'un dossier (son nom sur le serveur) existe encore.</param>
    /// <param name="roleExists">Sur un serveur qui active les rôles par cabinet : dit si le rôle du cabinet existe encore.</param>
    public static async Task OnboardThenLeaveAsync(
        CatalogHarness harness, ManualClock clock, Vault vault, Identity identity, Func<string, Task<bool>> databaseExists, Func<Guid, Task<bool>>? roleExists = null)
    {
        IServiceProvider services = harness.Services;
        await services.InitializeCatalogAsync(migrate: true, default);
        ICabinetLifecycle lifecycle = services.GetRequiredService<ICabinetLifecycle>();
        IDossierErasurePass pass = services.GetRequiredService<IDossierErasurePass>();
        IDossierProvisioner provisioner = services.GetRequiredService<IDossierProvisioner>();
        lifecycle.IsAvailable.Should().BeTrue();
        (await lifecycle.ListCellsAsync(default)).Should().Equal("cellule-test");

        // Arrivée : inscrit à l'identique une seconde fois, avec sa clé.
        OrganisationId cabinet = new(Guid.CreateVersion7());
        PlatformCabinetResponse registered = (await lifecycle.RegisterAsync(cabinet, "Cabinet qui part", "cellule-test", default)).Value;
        registered.Should().BeEquivalentTo(new { Name = "Cabinet qui part", CellId = "cellule-test", State = PlatformCabinetStateDto.Active, Declared = false });
        (await lifecycle.RegisterAsync(cabinet, "Cabinet qui part", "cellule-test", default)).Value.Should().BeEquivalentTo(registered);
        (await services.GetRequiredService<IKekStore>().FindAsync(cabinet, default)).Should().NotBeNull();

        // Une personne, une équipe, un rôle et un dossier : tout doit disparaître au départ.
        Guid person = Guid.CreateVersion7();
        IAuthorizationAdministration administration = services.GetRequiredService<IAuthorizationAdministration>();
        await administration.SetAccountAsync(cabinet, person, AccountState.Active, default);
        await services.GetRequiredService<IAuthorizationRegistry>().UpdatePersonAsync(cabinet, person, "Camille Martin", "camille@cabinet.example", default);
        await administration.AssignAsync(cabinet, new RoleAssignment(new SubjectRef(SubjectKind.User, person), RoleTemplates.AdministrateurCabinet, AuthorizationScope.ForCabinet(cabinet)), default);
        GroupId team = (await administration.CreateGroupAsync(cabinet, "Pôle social", default)).Value;
        await administration.SetGroupMemberAsync(team, person, member: true, default);
        DossierId dossier = (await provisioner.CreateAsync(cabinet, "Dossier du cabinet", default)).Value;
        string database = (await harness.DossierAsync(dossier)).DatabaseName!;
        (await databaseExists(database)).Should().BeTrue();
        if (roleExists is not null)
        {
            (await roleExists(cabinet.Value)).Should().BeTrue("la base du dossier appartient au rôle du cabinet");
        }

        // Un cabinet déclaré dans la configuration, ou inconnu, ne part pas par l'API.
        (await lifecycle.ScheduleDepartureAsync(CatalogHarness.Organisation, Guid.Empty, default)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(PlatformErrors.CabinetDeclaredCode);
        (await lifecycle.ScheduleDepartureAsync(new OrganisationId(Guid.CreateVersion7()), Guid.Empty, default)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(DossierErrors.OrganisationNotFoundCode);
        (await lifecycle.CancelDepartureAsync(new OrganisationId(Guid.CreateVersion7()), default)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(DossierErrors.OrganisationNotFoundCode);

        // Départ demandé : le cabinet travaille encore, sans nouveau dossier ; annulable pendant le délai.
        Guid platformAdmin = Guid.CreateVersion7();
        PlatformCabinetResponse leaving = (await lifecycle.ScheduleDepartureAsync(cabinet, platformAdmin, default)).Value;
        leaving.State.Should().Be(PlatformCabinetStateDto.Leaving);
        leaving.DepartureDueAt.Should().Be(Start.AddDays(30));
        leaving.ActiveDossiers.Should().Be(1);
        (await lifecycle.ScheduleDepartureAsync(cabinet, platformAdmin, default)).Value.DepartureDueAt.Should().Be(Start.AddDays(30), "redemander ne repousse pas le départ");
        (await services.GetRequiredService<ITenantCatalog>().FindAsync(dossier, default)).Should().NotBeNull("le cabinet exporte ses données pendant le délai");
        (await provisioner.CreateAsync(cabinet, "Dossier de trop", default)).Errors.Should().ContainSingle().Which.Code.Should().Be(PlatformErrors.CabinetLeavingCode);
        (await lifecycle.RegisterAsync(cabinet, "Cabinet qui part", "cellule-test", default)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(PlatformErrors.CabinetLeavingCode);
        (await lifecycle.CancelDepartureAsync(cabinet, default)).Value.State.Should().Be(PlatformCabinetStateDto.Active);
        (await lifecycle.CancelDepartureAsync(cabinet, default)).Errors.Should().ContainSingle().Which.Code.Should().Be(PlatformErrors.DepartureNotCancellableCode);

        // Redemandé : rien ne part avant la fin du délai.
        await lifecycle.ScheduleDepartureAsync(cabinet, platformAdmin, default);
        clock.Now = Start.AddDays(29);
        (await pass.EraseDueAsync(default)).Should().Be(0);
        vault.Destroyed.Should().BeEmpty();

        // Délai passé : plus d'annulation ; dossiers effacés, clé détruite, comptes supprimés, registre vidé.
        clock.Now = Start.AddDays(30).AddMinutes(1);
        (await lifecycle.CancelDepartureAsync(cabinet, default)).Errors.Should().ContainSingle().Which.Code.Should().Be(PlatformErrors.DepartureNotCancellableCode);
        (await pass.EraseDueAsync(default)).Should().Be(1);
        (await databaseExists(database)).Should().BeFalse();
        (await harness.DossierAsync(dossier)).State.Should().Be(DossierState.Deleted);
        vault.Destroyed.Should().Equal(cabinet);
        identity.Deleted.Should().Equal(cabinet);
        if (roleExists is not null)
        {
            (await roleExists(cabinet.Value)).Should().BeFalse("le rôle d'un cabinet parti est supprimé");
        }
        await using (CatalogDbContext db = await services.GetRequiredService<IDbContextFactory<CatalogDbContext>>().CreateDbContextAsync())
        {
            (await db.UserAccounts.AnyAsync(u => u.OrganisationId == cabinet.Value)).Should().BeFalse();
            (await db.Groups.AnyAsync(g => g.OrganisationId == cabinet.Value)).Should().BeFalse();
            (await db.GroupMembers.AnyAsync(m => m.UserId == person)).Should().BeFalse();
            (await db.RoleAssignments.AnyAsync(a => a.SubjectId == person)).Should().BeFalse();
            CatalogOrganisation trace = await db.Organisations.AsNoTracking().SingleAsync(o => o.Id == cabinet.Value);
            trace.State.Should().Be(OrganisationState.Departed);
            trace.WrappedKek.Should().BeNull();
            trace.DepartedAt.Should().NotBeNull();

            // Le cabinet déclaré, lui, garde tout.
            (await db.UserAccounts.AnyAsync(u => u.Id == CatalogHarness.Member)).Should().BeTrue();
        }

        PlatformCabinetResponse departed = (await lifecycle.ListAsync(default)).Single(c => c.Id == cabinet.Value);
        departed.State.Should().Be(PlatformCabinetStateDto.Departed);
        departed.DepartedAt.Should().NotBeNull();
        (await lifecycle.ListAsync(default)).Single(c => c.Id == CatalogHarness.Organisation.Value).Declared.Should().BeTrue();

        // Une seule fois ; un cabinet parti ne repart pas et ne reçoit plus de dossier.
        (await pass.EraseDueAsync(default)).Should().Be(0);
        vault.Destroyed.Should().ContainSingle();
        (await lifecycle.ScheduleDepartureAsync(cabinet, platformAdmin, default)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(DossierErrors.OrganisationNotFoundCode);
        (await lifecycle.CancelDepartureAsync(cabinet, default)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(DossierErrors.OrganisationNotFoundCode);
        (await provisioner.CreateAsync(cabinet, "Dossier posthume", default)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(DossierErrors.OrganisationNotFoundCode);
        (await lifecycle.RetryFailedDossiersAsync(default)).Should().Be(0);
    }

    /// <summary>Un départ ne se termine pas sans le fournisseur d'identité qui a créé les comptes : il attend.</summary>
    public static async Task DepartureWaitsForIdentityAsync(CatalogHarness harness, ManualClock clock, Vault vault, Identity identity)
    {
        IServiceProvider services = harness.Services;
        await services.InitializeCatalogAsync(migrate: true, default);
        ICabinetLifecycle lifecycle = services.GetRequiredService<ICabinetLifecycle>();
        OrganisationId cabinet = new(Guid.CreateVersion7());
        await lifecycle.RegisterAsync(cabinet, "Cabinet en attente", "cellule-test", default);
        await lifecycle.ScheduleDepartureAsync(cabinet, Guid.Empty, default);
        clock.Now = Start.AddDays(31);

        identity.IsConfigured = false;
        await services.GetRequiredService<IDossierErasurePass>().EraseDueAsync(default);
        vault.Destroyed.Should().BeEmpty();
        (await lifecycle.ListAsync(default)).Single(c => c.Id == cabinet.Value).State.Should().Be(PlatformCabinetStateDto.Leaving);

        // Le KMS en panne : le départ est repris à la passe suivante.
        identity.IsConfigured = true;
        vault.Failure = new HttpRequestException("KMS injoignable");
        await services.GetRequiredService<IDossierErasurePass>().EraseDueAsync(default);
        (await lifecycle.ListAsync(default)).Single(c => c.Id == cabinet.Value).State.Should().Be(PlatformCabinetStateDto.Leaving);

        vault.Failure = null;
        await services.GetRequiredService<IDossierErasurePass>().EraseDueAsync(default);
        (await lifecycle.ListAsync(default)).Single(c => c.Id == cabinet.Value).State.Should().Be(PlatformCabinetStateDto.Departed);
    }

    internal sealed class Vault : ICabinetKeyVault
    {
        public List<OrganisationId> Destroyed { get; } = [];

        public Exception? Failure { get; set; }

        public Task<bool> RewrapAsync(OrganisationId organisation, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<int> RewrapAllAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public Task RotateAsync(OrganisationId organisation, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DestroyAsync(OrganisationId organisation, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            Destroyed.Add(organisation);
            return Task.CompletedTask;
        }
    }

    internal sealed class Identity : IIdentityProvisioning
    {
        public bool IsConfigured { get; set; } = true;

        public List<OrganisationId> Deleted { get; } = [];

        public Task<Result<OrganisationId>> EnsureOrganisationAsync(string alias, string name, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<ProvisionedAccount>> EnsureMemberAsync(OrganisationId organisation, string email, string displayName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteOrganisationAsync(OrganisationId organisation, CancellationToken cancellationToken)
        {
            Deleted.Add(organisation);
            return Task.CompletedTask;
        }
    }
}
