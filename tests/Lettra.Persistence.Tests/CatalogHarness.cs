using System.Globalization;
using System.Security.Cryptography;
using Lettra.Application;
using Lettra.Application.Abstractions.External;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Directory;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Catalog;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Catalog;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Monte la base Catalog réelle (modes 2, 3 et 5) sur un provider : un cabinet, un serveur de bases, la création
/// des bases de dossiers et les vagues de migration.
/// </summary>
internal sealed class CatalogHarness : IAsyncDisposable
{
    public static readonly OrganisationId Organisation = new(new Guid("0199e3a0-0000-7000-8000-0000000000c1"));

    public static readonly Guid OtherCabinet = new("0199e3a0-0000-7000-8000-0000000000c2");

    public static readonly Guid Member = new("0199e3a0-0000-7000-8000-00000000e001");

    public static readonly Guid Group = new("0199e3a0-0000-7000-8000-00000000a001");

    public static readonly Guid ForeignGroup = new("0199e3a0-0000-7000-8000-00000000a002");

    public static readonly Guid AuditedDossier = new("0199e3a0-0000-7000-8000-00000000d0d1");

    private readonly ServiceProvider _root;

    public CatalogHarness(
        Action<IServiceCollection> addProvider,
        Action<DbContextOptionsBuilder> configureCatalog,
        IDictionary<string, string?> cluster)
    {
        Dictionary<string, string?> settings = new()
        {
            ["Lettra:Organisations:0:Id"] = Organisation.Value.ToString(),
            ["Lettra:Organisations:0:Name"] = "Cabinet de test",
            ["Lettra:Organisations:0:CellId"] = "cellule-test",
            ["Lettra:Organisations:0:Kek"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Lettra:Clusters:srv-a:CellId"] = "cellule-test",
            ["Lettra:Clusters:srv-a:MaxDatabases"] = "100",

            // Registre des droits déclaré : un membre de deux groupes, dont un d'un autre cabinet (ignoré).
            ["Lettra:Authorization:Users:0:Id"] = Member.ToString(),
            ["Lettra:Authorization:Users:0:Cabinet"] = Organisation.Value.ToString(),
            ["Lettra:Authorization:Users:0:Groups:0"] = Group.ToString(),
            ["Lettra:Authorization:Users:0:Groups:1"] = ForeignGroup.ToString(),
            ["Lettra:Authorization:Users:0:Assignments:0:Role"] = "auditeur",
            ["Lettra:Authorization:Users:0:Assignments:0:Scope"] = "Dossier",
            ["Lettra:Authorization:Users:0:Assignments:0:ScopeId"] = AuditedDossier.ToString(),
            ["Lettra:Authorization:Users:0:Assignments:0:ExpiresAt"] = "2027-01-31T00:00:00Z",
            ["Lettra:Authorization:Groups:0:Id"] = Group.ToString(),
            ["Lettra:Authorization:Groups:0:Cabinet"] = Organisation.Value.ToString(),
            ["Lettra:Authorization:Groups:0:Name"] = "Pôle social",
            ["Lettra:Authorization:Groups:0:Assignments:0:Role"] = "collaborateur",
            ["Lettra:Authorization:Groups:0:Assignments:0:Scope"] = "Groupe",
            ["Lettra:Authorization:Groups:0:Assignments:0:ScopeId"] = Group.ToString(),
            ["Lettra:Authorization:Groups:1:Id"] = ForeignGroup.ToString(),
            ["Lettra:Authorization:Groups:1:Cabinet"] = OtherCabinet.ToString(),
            ["Lettra:Authorization:Groups:1:Assignments:0:Role"] = "manager",
            ["Lettra:Authorization:Groups:1:Assignments:0:Scope"] = "Groupe",
            ["Lettra:Authorization:Groups:1:Assignments:0:ScopeId"] = ForeignGroup.ToString(),
            ["Lettra:Authorization:Roles:0:Cabinet"] = Organisation.Value.ToString(),
            ["Lettra:Authorization:Roles:0:Key"] = "stagiaire",
            ["Lettra:Authorization:Roles:0:Permissions:0"] = "TiersConsulter",
            ["Lettra:Authorization:Roles:0:Permissions:1"] = "CatalogueConsulter",
            ["Lettra:Authorization:Roles:0:RequiresExpiry"] = "true",
        };
        foreach ((string key, string? value) in cluster)
        {
            settings[$"Lettra:Clusters:srv-a:{key}"] = value;
        }

        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddApplication();
        services.AddPersistenceCore();
        addProvider(services);
        services.AddCatalogDatabase(configuration, configureCatalog);
        services.AddSingleton<ICurrentUser, AnyUser>();
        services.AddSingleton<IPermissionService, NoPermissions>();
        services.AddScoped<IRequestedDossierSource, NoRequestedDossier>();
        services.AddSingleton<OfflineDirectories>();
        services.AddSingleton<ICompanyDirectory>(sp => sp.GetRequiredService<OfflineDirectories>());
        services.AddSingleton<IVatNumberChecker>(sp => sp.GetRequiredService<OfflineDirectories>());
        _root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public IServiceProvider Services => _root;

    public async Task<CatalogDossier> DossierAsync(DossierId id)
    {
        await using CatalogDbContext db = await _root.GetRequiredService<IDbContextFactory<CatalogDbContext>>().CreateDbContextAsync();
        return await db.Dossiers.AsNoTracking().SingleAsync(d => d.Id == id.Value);
    }

    public async Task UpdateDossierAsync(DossierId id, Action<CatalogDossier> change)
    {
        await using CatalogDbContext db = await _root.GetRequiredService<IDbContextFactory<CatalogDbContext>>().CreateDbContextAsync();
        CatalogDossier dossier = await db.Dossiers.SingleAsync(d => d.Id == id.Value);
        change(dossier);
        await db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync() => _root.DisposeAsync();

    private sealed class AnyUser : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public Guid UserId { get; } = Guid.CreateVersion7();
    }

    /// <summary>Annuaires externes hors sujet ici : toujours indisponibles.</summary>
    private sealed class OfflineDirectories : ICompanyDirectory, IVatNumberChecker
    {
        public Task<Result<CompanySearchResponse>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken) =>
            Task.FromResult(Result<CompanySearchResponse>.Failure(DirectoryErrors.Unavailable));

        public Task<Result<VatNumberCheckResponse>> CheckAsync(VatNumber vatNumber, CancellationToken cancellationToken) =>
            Task.FromResult(Result<VatNumberCheckResponse>.Failure(DirectoryErrors.Unavailable));
    }

    private sealed class NoPermissions : IPermissionService
    {
        public ValueTask<IReadOnlySet<Permission>> GetEffectiveAsync(Guid userId, AuthorizationScope scope, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlySet<Permission>>(new HashSet<Permission>());
    }
}

/// <summary>Création de dossier, reprise et vagues de migration : joués à l'identique sur chaque provider.</summary>
internal static class CatalogScenarios
{
    public static async Task ProvisionAndMigrateAsync(CatalogHarness harness, DatabaseProvider provider)
    {
        IServiceProvider services = harness.Services;
        await using (CatalogDbContext db = await services.GetRequiredService<IDbContextFactory<CatalogDbContext>>().CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();
        }

        // Plusieurs instances qui démarrent ensemble importent la même configuration sans conflit.
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => services.InitializeCatalogAsync(migrate: false, default))));
        // Puis encore une fois : l'import de la configuration est idempotent.
        await services.InitializeCatalogAsync(migrate: true, default);

        Result<DossierId> created = await Create(services, "Dossier A");
        created.IsSuccess.Should().BeTrue();
        DossierId id = created.Value;

        CatalogDossier dossier = await harness.DossierAsync(id);
        string? latest = services.GetRequiredService<IDossierDbContextFactory>().LatestMigration(provider);
        dossier.State.Should().Be(DossierState.Active);
        dossier.Provider.Should().Be(provider);
        dossier.ClusterId.Should().Be("srv-a");
        dossier.SchemaVersion.Should().Be(latest).And.NotBeNull();
        dossier.WrappedDek.Should().HaveCount(60);
        dossier.ConnectionSecretRef.Should().Be($"cluster:srv-a/d_{id.Value:N}");

        // La base créée porte l'estampille du dossier : elle s'ouvre sous son identité.
        TenantDescriptor descriptor = (await services.GetRequiredService<ITenantCatalog>().FindAsync(id, default))!;
        await using (LettraDbContext opened = await services.GetRequiredService<IDossierDbContextFactory>().CreateAsync(descriptor, verifyStamp: true, default))
        {
            (await opened.Customers.CountAsync()).Should().Be(0);
        }

        (await services.GetRequiredService<IDossierDirectory>().ListAsync(CatalogHarness.Organisation, default))
            .Should().ContainSingle(d => d.Id == id.Value && d.Name == "Dossier A");

        // Une base en retard est reprise par la vague ; une base sous bail d'un autre Worker est laissée.
        DossierId leased = (await Create(services, "Dossier B")).Value;
        await harness.UpdateDossierAsync(id, d => d.SchemaVersion = "20261014000001_InitialCustomers");
        await harness.UpdateDossierAsync(leased, d =>
        {
            d.SchemaVersion = "20261014000001_InitialCustomers";
            d.LeaseOwner = "autre-worker";
            d.LeaseUntil = DateTime.UtcNow.AddMinutes(5);
        });

        DossierMigrationOrchestrator orchestrator = services.GetRequiredService<DossierMigrationOrchestrator>();
        MigrationWaveReport wave = await orchestrator.RunWaveAsync(new MigrationWaveOptions { Owner = "worker-test" }, default);

        wave.Should().BeEquivalentTo(new { Candidates = 2, Migrated = 1, Failed = 0, SkippedLeased = 1, Aborted = false });
        (await harness.DossierAsync(id)).Should().BeEquivalentTo(new { SchemaVersion = latest, LeaseOwner = (string?)null, LastError = (string?)null });

        // Un dossier « en création » (processus arrêté avant la base) est terminé par la reprise.
        await harness.UpdateDossierAsync(leased, d => d.State = DossierState.Provisioning);
        (await services.ResumeDossierProvisioningAsync(default)).Should().Be(1);
        (await harness.DossierAsync(leased)).State.Should().Be(DossierState.Active);
    }

    /// <summary>Registre des droits dans la base Catalog : import de la configuration, profil et chemins de portée.</summary>
    public static async Task AuthorizationDirectoryAsync(CatalogHarness harness)
    {
        IServiceProvider services = harness.Services;
        await services.InitializeCatalogAsync(migrate: true, default);
        // Deux imports : ni attribution, ni appartenance, ni permission en double.
        await services.InitializeCatalogAsync(migrate: false, default);

        DossierId inGroup = (await Create(services, "Dossier du pôle")).Value;
        DossierId outOfGroup = (await Create(services, "Dossier hors pôle")).Value;
        await using (CatalogDbContext db = await services.GetRequiredService<IDbContextFactory<CatalogDbContext>>().CreateDbContextAsync())
        {
            db.GroupDossiers.Add(new CatalogGroupDossier { GroupId = CatalogHarness.Group, DossierId = inGroup.Value });
            // Un groupe d'un autre cabinet qui prétend contenir le dossier : ignoré à la lecture.
            db.GroupDossiers.Add(new CatalogGroupDossier { GroupId = CatalogHarness.ForeignGroup, DossierId = outOfGroup.Value });
            await db.SaveChangesAsync();
        }

        IAuthorizationDirectory directory = services.GetRequiredService<IAuthorizationDirectory>();
        UserAuthorizationProfile? profile = await directory.FindUserAsync(CatalogHarness.Member, default);
        profile.Should().NotBeNull();
        profile!.State.Should().Be(AccountState.Active);
        profile.Cabinet.Should().Be(CatalogHarness.Organisation);
        profile.Groups.Should().Equal(new GroupId(CatalogHarness.Group));
        profile.Assignments.Should().BeEquivalentTo(new[]
        {
            new RoleAssignment(new SubjectRef(SubjectKind.User, CatalogHarness.Member), "auditeur",
                new AuthorizationScope(ScopeKind.Dossier, CatalogHarness.AuditedDossier), DateTimeOffset.Parse("2027-01-31T00:00:00Z", CultureInfo.InvariantCulture)),
            new RoleAssignment(new SubjectRef(SubjectKind.Group, CatalogHarness.Group), "collaborateur",
                AuthorizationScope.ForGroup(new GroupId(CatalogHarness.Group))),
        });
        RoleDefinition stagiaire = profile.CustomRoles.Should().ContainSingle().Subject;
        stagiaire.Key.Should().Be("stagiaire");
        stagiaire.RequiresExpiry.Should().BeTrue();
        stagiaire.Permissions.Should().BeEquivalentTo(new[] { Permission.TiersConsulter, Permission.CatalogueConsulter });

        (await directory.FindUserAsync(Guid.CreateVersion7(), default)).Should().BeNull();

        AuthorizationScope cabinet = AuthorizationScope.ForCabinet(CatalogHarness.Organisation);
        (await directory.GetScopePathAsync(AuthorizationScope.ForDossier(inGroup), default))!.Ancestors.Should().Equal(
            AuthorizationScope.ForDossier(inGroup), AuthorizationScope.ForGroup(new GroupId(CatalogHarness.Group)), cabinet, AuthorizationScope.Plateforme);
        (await directory.GetScopePathAsync(AuthorizationScope.ForDossier(outOfGroup), default))!.Ancestors.Should().Equal(
            AuthorizationScope.ForDossier(outOfGroup), cabinet, AuthorizationScope.Plateforme);
        (await directory.GetScopePathAsync(AuthorizationScope.ForDossier(new DossierId(Guid.CreateVersion7())), default)).Should().BeNull();
        (await directory.GetScopePathAsync(AuthorizationScope.ForGroup(new GroupId(CatalogHarness.ForeignGroup)), default))!.Cabinet
            .Should().Be(new OrganisationId(CatalogHarness.OtherCabinet));
        (await directory.GetScopePathAsync(cabinet, default))!.Ancestors.Should().Equal(cabinet, AuthorizationScope.Plateforme);
        (await directory.GetScopePathAsync(AuthorizationScope.ForCabinet(new OrganisationId(Guid.CreateVersion7())), default)).Should().BeNull();

        // Tous les dossiers du cabinet situés d'un coup : mêmes chemins que dossier par dossier, l'inconnu omis.
        IReadOnlyList<ScopePath> paths = await directory.GetDossierPathsAsync(
            CatalogHarness.Organisation, [inGroup, outOfGroup, new DossierId(Guid.CreateVersion7())], default);
        paths.Select(p => p.Target).Should().BeEquivalentTo(new[] { AuthorizationScope.ForDossier(inGroup), AuthorizationScope.ForDossier(outOfGroup) });
        paths.Single(p => p.Target.Id == inGroup.Value).Ancestors.Should().Equal(
            AuthorizationScope.ForDossier(inGroup), AuthorizationScope.ForGroup(new GroupId(CatalogHarness.Group)), cabinet, AuthorizationScope.Plateforme);
        (await directory.GetDossierPathsAsync(new OrganisationId(CatalogHarness.OtherCabinet), [inGroup], default)).Should().BeEmpty();

        await AdministrationAsync(services, directory, outOfGroup);
    }

    /// <summary>Écriture du registre : chaque lien reste dans le cabinet, et les opérations répétées ne dupliquent rien.</summary>
    private static async Task AdministrationAsync(IServiceProvider services, IAuthorizationDirectory directory, DossierId dossier)
    {
        IAuthorizationAdministration admin = services.GetRequiredService<IAuthorizationAdministration>();
        OrganisationId cabinet = CatalogHarness.Organisation;
        Guid user = Guid.CreateVersion7();

        (await admin.SetAccountAsync(cabinet, user, AccountState.Active, default)).IsSuccess.Should().BeTrue();
        (await admin.SetAccountAsync(new OrganisationId(CatalogHarness.OtherCabinet), user, AccountState.Active, default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(RightsErrors.UserNotFoundCode);
        (await admin.CreateGroupAsync(new OrganisationId(Guid.CreateVersion7()), "Fantôme", default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(DossierErrors.OrganisationNotFoundCode);

        GroupId group = (await admin.CreateGroupAsync(cabinet, "Pôle social", default)).Value;
        for (int i = 0; i < 2; i++)
        {
            (await admin.SetGroupMemberAsync(group, user, member: true, default)).IsSuccess.Should().BeTrue();
            (await admin.SetDossierPlacementAsync(group, dossier, placed: true, default)).IsSuccess.Should().BeTrue();
        }

        (await admin.SetGroupMemberAsync(new GroupId(CatalogHarness.ForeignGroup), user, member: true, default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(RightsErrors.UserNotFoundCode);
        (await admin.SetDossierPlacementAsync(new GroupId(CatalogHarness.ForeignGroup), dossier, placed: true, default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(TenancyErrors.DossierNotFoundCode);

        RoleAssignment assignment = new(new SubjectRef(SubjectKind.User, user), "collaborateur", AuthorizationScope.ForGroup(group));
        Guid first = (await admin.AssignAsync(cabinet, assignment, default)).Value;
        DateTimeOffset expires = DateTimeOffset.Parse("2027-06-30T00:00:00Z", CultureInfo.InvariantCulture);
        (await admin.AssignAsync(cabinet, assignment with { ExpiresAt = expires }, default)).Value.Should().Be(first);
        (await admin.AssignAsync(new OrganisationId(CatalogHarness.OtherCabinet), assignment, default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(RightsErrors.SubjectNotFoundCode);

        UserAuthorizationProfile profile = (await directory.FindUserAsync(user, default))!;
        profile.Groups.Should().Equal(group);
        profile.Assignments.Should().ContainSingle().Which.ExpiresAt.Should().Be(expires);
        (await directory.GetScopePathAsync(AuthorizationScope.ForDossier(dossier), default))!.Ancestors.Should().Contain(AuthorizationScope.ForGroup(group));
        (await admin.FindCustomRoleAsync(cabinet, "stagiaire", default))!.RequiresExpiry.Should().BeTrue();
        (await admin.FindCustomRoleAsync(new OrganisationId(CatalogHarness.OtherCabinet), "stagiaire", default)).Should().BeNull();

        (await admin.RevokeAsync(new OrganisationId(CatalogHarness.OtherCabinet), first, default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(RightsErrors.AssignmentNotFoundCode);
        (await admin.RevokeAsync(cabinet, first, default)).Value.Should().Be(new SubjectRef(SubjectKind.User, user));
        (await admin.SetGroupMemberAsync(group, user, member: false, default)).IsSuccess.Should().BeTrue();
        (await admin.SetDossierPlacementAsync(group, dossier, placed: false, default)).IsSuccess.Should().BeTrue();
        (await admin.SetAccountAsync(cabinet, user, AccountState.Suspended, default)).IsSuccess.Should().BeTrue();

        profile = (await directory.FindUserAsync(user, default))!;
        profile.State.Should().Be(AccountState.Suspended);
        profile.Groups.Should().BeEmpty();
        profile.Assignments.Should().BeEmpty();
        (await directory.GetScopePathAsync(AuthorizationScope.ForDossier(dossier), default))!.Ancestors.Should().NotContain(AuthorizationScope.ForGroup(group));
    }

    /// <summary>Séparation des tâches (annexe F §3.2) : inactive par défaut, réglée puis modifiée par l'administrateur.</summary>
    public static async Task OrganisationPoliciesAsync(CatalogHarness harness)
    {
        await harness.Services.InitializeCatalogAsync(migrate: true, default);
        IOrganisationPolicyStore store = harness.Services.GetRequiredService<IOrganisationPolicyStore>();
        store.SupportsSegregationOfDuties.Should().BeTrue();
        (await store.GetSegregationOfDutiesAsync(CatalogHarness.Organisation, default)).Should().Be(SegregationOfDutiesPolicy.Disabled);

        Guid admin = Guid.CreateVersion7();
        await store.SaveSegregationOfDutiesAsync(CatalogHarness.Organisation, new SegregationOfDutiesPolicy(true, 1500.50m), admin, default);
        (await store.GetSegregationOfDutiesAsync(CatalogHarness.Organisation, default)).Should().Be(new SegregationOfDutiesPolicy(true, 1500.50m));

        await store.SaveSegregationOfDutiesAsync(CatalogHarness.Organisation, new SegregationOfDutiesPolicy(true, null), admin, default);
        (await store.GetSegregationOfDutiesAsync(CatalogHarness.Organisation, default)).Should().Be(new SegregationOfDutiesPolicy(true, null));

        await using CatalogDbContext db = await harness.Services.GetRequiredService<IDbContextFactory<CatalogDbContext>>().CreateDbContextAsync();
        CatalogOrganisationPolicy row = await db.OrganisationPolicies.SingleAsync();
        row.UpdatedBy.Should().Be(admin);
    }

    public static async Task UnknownOrganisationAsync(CatalogHarness harness)
    {
        await harness.Services.InitializeCatalogAsync(migrate: true, default);
        Result<DossierId> result = await harness.Services.GetRequiredService<IDossierProvisioner>()
            .CreateAsync(new OrganisationId(Guid.CreateVersion7()), "X", default);
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(DossierErrors.OrganisationNotFoundCode);
    }

    private static async Task<Result<DossierId>> Create(IServiceProvider services, string name) =>
        await services.GetRequiredService<IDossierProvisioner>().CreateAsync(CatalogHarness.Organisation, name, default);
}

/// <summary>Hors requête : aucun dossier demandé.</summary>
internal sealed class NoRequestedDossier : Lettra.Application.Abstractions.Tenancy.IRequestedDossierSource
{
    public Lettra.Domain.SharedKernel.DossierId? RequestedDossier => null;
}
