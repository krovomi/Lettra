using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Identity.Oidc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lettra.Identity.Tests;

/// <summary>
/// Le registre des droits lu en configuration, interrogé directement : profil d'un utilisateur (groupes et rôles de
/// son cabinet seulement), chemin d'une portée jusqu'à la plateforme, et candidats d'une portée pour le routage.
/// </summary>
public sealed class ConfigurationDirectoryTests
{
    private static readonly Guid Cabinet = Guid.CreateVersion7();
    private static readonly Guid OtherCabinet = Guid.CreateVersion7();
    private static readonly Guid Group = Guid.CreateVersion7();
    private static readonly Guid SecondGroup = Guid.CreateVersion7();
    private static readonly Guid ForeignGroup = Guid.CreateVersion7();
    private static readonly Guid Dossier = Guid.CreateVersion7();
    private static readonly Guid ForeignDossier = Guid.CreateVersion7();
    private static readonly DateTimeOffset Expiry = new(2026, 12, 31, 23, 0, 0, TimeSpan.Zero);

    private static readonly Guid Member = Guid.CreateVersion7();
    private static readonly Guid Direct = Guid.CreateVersion7();
    private static readonly Guid Suspended = Guid.CreateVersion7();
    private static readonly Guid Elsewhere = Guid.CreateVersion7();
    private static readonly Guid Operator = Guid.CreateVersion7();
    private static readonly Guid Stranger = Guid.CreateVersion7();
    private static readonly Guid Homeless = Guid.CreateVersion7();

    private static AuthorizationDirectoryOptions Options() => new()
    {
        Users =
        {
            new UserEntry
            {
                Id = Member, Cabinet = Cabinet, Groups = { Group, ForeignGroup, Guid.CreateVersion7() },
                Assignments = { new AssignmentEntry { Role = "auditeur", Scope = ScopeKind.Dossier, ScopeId = Dossier, ExpiresAt = Expiry } },
            },
            new UserEntry { Id = Direct, Cabinet = Cabinet, Assignments = { new AssignmentEntry { Role = "collaborateur", ScopeId = Dossier } } },
            new UserEntry
            {
                Id = Suspended, Cabinet = Cabinet, State = AccountState.Suspended,
                Assignments = { new AssignmentEntry { Role = "collaborateur", ScopeId = Dossier } },
            },
            new UserEntry { Id = Elsewhere, Cabinet = Cabinet, Assignments = { new AssignmentEntry { Role = "collaborateur", ScopeId = Guid.CreateVersion7() } } },

            // Une attribution plateforme ignore l'identifiant écrit à côté.
            new UserEntry { Id = Operator, Assignments = { new AssignmentEntry { Role = "exploitant", Scope = ScopeKind.Plateforme, ScopeId = Guid.CreateVersion7() } } },

            // Membre déclaré d'un groupe d'un autre cabinet : le groupe ne compte pas.
            new UserEntry { Id = Stranger, Cabinet = OtherCabinet, Groups = { Group } },
            new UserEntry { Id = Homeless, Groups = { Group } },
        },
        Groups =
        {
            new GroupEntry
            {
                Id = Group, Cabinet = Cabinet, Dossiers = { Dossier },
                Assignments = { new AssignmentEntry { Role = "collaborateur", Scope = ScopeKind.Groupe, ScopeId = Group } },
            },
            new GroupEntry { Id = SecondGroup, Cabinet = Cabinet, Dossiers = { Dossier, Guid.CreateVersion7() } },
            new GroupEntry
            {
                Id = ForeignGroup, Cabinet = OtherCabinet, Dossiers = { Dossier, ForeignDossier },
                Assignments = { new AssignmentEntry { Role = "manager", Scope = ScopeKind.Groupe, ScopeId = ForeignGroup } },
            },
        },
        Roles =
        {
            new CustomRoleEntry
            {
                Cabinet = Cabinet, Key = "relecteur", Permissions = { Permission.FactureSaisir, Permission.FactureSaisir, Permission.TiersConsulter },
                WidestScope = ScopeKind.Groupe, RequiresExpiry = true,
            },
            new CustomRoleEntry { Cabinet = OtherCabinet, Key = "etranger", Permissions = { Permission.FactureValider } },
        },
    };

    private static ConfigurationAuthorizationDirectory Directory(AuthorizationDirectoryOptions? options = null) =>
        new(new StaticMonitor(options ?? Options()), new Catalog(new Dictionary<Guid, Guid> { [Dossier] = Cabinet, [ForeignDossier] = OtherCabinet }));

    [Fact]
    public async Task Un_utilisateur_absent_du_registre_n_a_pas_de_profil() =>
        (await Directory().FindUserAsync(Guid.CreateVersion7(), default)).Should().BeNull();

    [Fact]
    public async Task Le_profil_ne_garde_que_les_groupes_et_les_roles_du_cabinet_de_l_utilisateur()
    {
        UserAuthorizationProfile profile = (await Directory().FindUserAsync(Member, default))!;

        profile.UserId.Should().Be(Member);
        profile.State.Should().Be(AccountState.Active);
        profile.Cabinet.Should().Be(new OrganisationId(Cabinet));
        profile.Groups.Should().Equal(new GroupId(Group));
        profile.Assignments.Should().BeEquivalentTo(
        [
            new RoleAssignment(new SubjectRef(SubjectKind.User, Member), "auditeur", AuthorizationScope.ForDossier(new DossierId(Dossier)), Expiry),
            new RoleAssignment(new SubjectRef(SubjectKind.Group, Group), "collaborateur", AuthorizationScope.ForGroup(new GroupId(Group))),
        ]);
        RoleDefinition role = profile.CustomRoles.Should().ContainSingle().Subject;
        role.Key.Should().Be("relecteur");
        role.Permissions.Should().BeEquivalentTo([Permission.FactureSaisir, Permission.TiersConsulter]);
        role.WidestScope.Should().Be(ScopeKind.Groupe);
        role.RequiresExpiry.Should().BeTrue();
    }

    [Fact]
    public async Task Une_attribution_plateforme_a_toujours_l_identifiant_vide()
    {
        UserAuthorizationProfile profile = (await Directory().FindUserAsync(Operator, default))!;

        profile.Cabinet.Should().BeNull();
        profile.Groups.Should().BeEmpty();
        profile.CustomRoles.Should().BeEmpty();
        profile.Assignments.Should().ContainSingle().Which.Scope.Should().Be(AuthorizationScope.Plateforme);
    }

    [Theory]
    [MemberData(nameof(StrangerOrHomeless))]
    public async Task Un_groupe_d_un_autre_cabinet_ou_sans_cabinet_n_est_pas_retenu(Guid user)
    {
        UserAuthorizationProfile profile = (await Directory().FindUserAsync(user, default))!;

        profile.Groups.Should().BeEmpty();
        profile.Assignments.Should().BeEmpty();
    }

    public static TheoryData<Guid> StrangerOrHomeless => new() { Stranger, Homeless };

    [Fact]
    public async Task Chemin_de_la_plateforme_et_d_un_cabinet()
    {
        ConfigurationAuthorizationDirectory directory = Directory();
        AuthorizationScope cabinet = AuthorizationScope.ForCabinet(new OrganisationId(Cabinet));

        ScopePath platform = (await directory.GetScopePathAsync(AuthorizationScope.Plateforme, default))!;
        platform.Cabinet.Should().BeNull();
        platform.Ancestors.Should().Equal(AuthorizationScope.Plateforme);

        ScopePath ofCabinet = (await directory.GetScopePathAsync(cabinet, default))!;
        ofCabinet.Target.Should().Be(cabinet);
        ofCabinet.Cabinet.Should().Be(new OrganisationId(Cabinet));
        ofCabinet.Ancestors.Should().Equal(cabinet, AuthorizationScope.Plateforme);
    }

    [Fact]
    public async Task Chemin_d_un_groupe_par_son_cabinet()
    {
        ConfigurationAuthorizationDirectory directory = Directory();
        AuthorizationScope group = AuthorizationScope.ForGroup(new GroupId(Group));

        ScopePath path = (await directory.GetScopePathAsync(group, default))!;

        path.Cabinet.Should().Be(new OrganisationId(Cabinet));
        path.Ancestors.Should().Equal(group, AuthorizationScope.ForCabinet(new OrganisationId(Cabinet)), AuthorizationScope.Plateforme);
        (await directory.GetScopePathAsync(AuthorizationScope.ForGroup(new GroupId(Guid.CreateVersion7())), default)).Should().BeNull();
    }

    [Fact]
    public async Task Chemin_d_un_dossier_par_les_groupes_de_son_cabinet_seulement()
    {
        ConfigurationAuthorizationDirectory directory = Directory();
        AuthorizationScope dossier = AuthorizationScope.ForDossier(new DossierId(Dossier));

        ScopePath path = (await directory.GetScopePathAsync(dossier, default))!;

        path.Cabinet.Should().Be(new OrganisationId(Cabinet));
        path.Ancestors.Should().Equal(
            dossier,
            AuthorizationScope.ForGroup(new GroupId(Group)),
            AuthorizationScope.ForGroup(new GroupId(SecondGroup)),
            AuthorizationScope.ForCabinet(new OrganisationId(Cabinet)),
            AuthorizationScope.Plateforme);

        ScopePath foreign = (await directory.GetScopePathAsync(AuthorizationScope.ForDossier(new DossierId(ForeignDossier)), default))!;
        foreign.Ancestors.Should().Contain(AuthorizationScope.ForGroup(new GroupId(ForeignGroup)));
        (await directory.GetScopePathAsync(AuthorizationScope.ForDossier(new DossierId(Guid.CreateVersion7())), default)).Should().BeNull();
    }

    [Fact]
    public async Task Une_portee_d_un_genre_inconnu_n_a_pas_de_chemin() =>
        (await Directory().GetScopePathAsync(new AuthorizationScope((ScopeKind)99, Guid.CreateVersion7()), default)).Should().BeNull();

    [Fact]
    public async Task Candidats_d_un_dossier_actifs_attribues_directement_ou_par_un_groupe_du_cabinet()
    {
        ConfigurationAuthorizationDirectory directory = Directory();
        ScopePath path = (await directory.GetScopePathAsync(AuthorizationScope.ForDossier(new DossierId(Dossier)), default))!;

        IReadOnlyCollection<Guid> candidates = await directory.ListCandidatesAsync(path, default);

        candidates.Should().BeEquivalentTo([Member, Direct, Operator], "suspendu, attribué ailleurs ou membre d'un groupe étranger : écartés");
    }

    [Fact]
    public async Task Candidats_d_un_dossier_etranger_seulement_l_exploitant()
    {
        // Le membre est inscrit au groupe étranger qui contient ce dossier, mais ce groupe n'est pas de son cabinet.
        ConfigurationAuthorizationDirectory directory = Directory();
        ScopePath path = (await directory.GetScopePathAsync(AuthorizationScope.ForDossier(new DossierId(ForeignDossier)), default))!;

        (await directory.ListCandidatesAsync(path, default)).Should().BeEquivalentTo([Operator]);
    }

    [Fact]
    public async Task Candidats_sans_chemin_refuse()
    {
        Func<Task> list = async () => await Directory().ListCandidatesAsync(null!, default);
        await list.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Le_registre_relit_la_configuration_a_chaque_appel()
    {
        AuthorizationDirectoryOptions options = Options();
        StaticMonitor monitor = new(options);
        ConfigurationAuthorizationDirectory directory = new(monitor, new Catalog([]));
        (await directory.FindUserAsync(Member, default)).Should().NotBeNull();

        monitor.CurrentValue = new AuthorizationDirectoryOptions();

        (await directory.FindUserAsync(Member, default)).Should().BeNull();
    }

    [Fact]
    public async Task Le_registre_se_lit_depuis_la_section_Lettra_Authorization()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lettra:Authorization:Users:0:Id"] = Direct.ToString(),
            ["Lettra:Authorization:Users:0:State"] = "Locked",
            ["Lettra:Authorization:Users:0:Assignments:0:Role"] = "exploitant",
            ["Lettra:Authorization:Users:0:Assignments:0:Scope"] = "Plateforme",
            ["Lettra:Authorization:Users:0:Assignments:0:ExpiresAt"] = "2026-12-31T23:00:00Z",
        }).Build();
        ServiceCollection services = new();
        services.AddSingleton<ITenantCatalog>(new Catalog([]));
        services.AddOidcIdentity(configuration);
        await using ServiceProvider provider = services.BuildServiceProvider();

        UserAuthorizationProfile profile = (await provider.GetRequiredService<IAuthorizationDirectory>().FindUserAsync(Direct, default))!;

        profile.State.Should().Be(AccountState.Locked);
        profile.Assignments.Should().ContainSingle().Which.Should().Be(
            new RoleAssignment(new SubjectRef(SubjectKind.User, Direct), "exploitant", AuthorizationScope.Plateforme, Expiry));
        provider.GetRequiredService<IScopeMemberDirectory>().Should().BeOfType<ConfigurationAuthorizationDirectory>();
    }

    [Fact]
    public void Valeurs_par_defaut_des_entrees()
    {
        new AssignmentEntry().Should().Match<AssignmentEntry>(a => a.Role == string.Empty && a.Scope == ScopeKind.Dossier && a.ScopeId == Guid.Empty && a.ExpiresAt == null);
        new UserEntry().Should().Match<UserEntry>(u => u.State == AccountState.Active && u.Cabinet == null && u.Groups.Count == 0 && u.Assignments.Count == 0);
        new CustomRoleEntry().Should().Match<CustomRoleEntry>(r => r.WidestScope == ScopeKind.Dossier && !r.RequiresExpiry && r.Key == string.Empty);
        AuthorizationDirectoryOptions.SectionName.Should().Be("Lettra:Authorization");
    }

    private sealed class StaticMonitor(AuthorizationDirectoryOptions value) : IOptionsMonitor<AuthorizationDirectoryOptions>
    {
        public AuthorizationDirectoryOptions CurrentValue { get; set; } = value;

        public AuthorizationDirectoryOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<AuthorizationDirectoryOptions, string?> listener) => null;
    }

    private sealed class Catalog(Dictionary<Guid, Guid> dossiers) : ITenantCatalog
    {
        public ValueTask<TenantDescriptor?> FindAsync(DossierId dossierId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(dossiers.TryGetValue(dossierId.Value, out Guid cabinet)
                ? new TenantDescriptor(dossierId, new OrganisationId(cabinet), DatabaseProvider.Sqlite, "config:x")
                : null);

        public ValueTask<bool> UserHasAccessAsync(Guid userId, DossierId dossierId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);
    }
}
