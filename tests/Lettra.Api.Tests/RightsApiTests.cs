using System.Net;
using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Dossiers;
using Lettra.Contracts.Rights;
using Lettra.Contracts.Serialization;

namespace Lettra.Api.Tests;

/// <summary>
/// Administration des droits de bout en bout : chaque écriture du registre prend effet tout de suite (cache des
/// permissions invalidé), et la liste des dossiers accessibles suit.
/// </summary>
public sealed class RightsApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static readonly Uri MyDossiers = new("/api/v1/dossiers", UriKind.Relative);

    private static Uri Cabinet(string path, Guid? cabinet = null) =>
        new($"/api/v1/cabinets/{cabinet ?? LettraApiFactory.CabinetId}/{path}", UriKind.Relative);

    private static Uri Group(Guid group, string path) => new($"/api/v1/groupes/{group}/{path}", UriKind.Relative);

    private HttpClient Admin() => factory.ClientFor(LettraApiFactory.AdministrateurCabinetId);

    private static async Task<Guid> NewAccountAsync(HttpClient admin)
    {
        Guid user = Guid.CreateVersion7();
        (await admin.PutAsJsonAsync(Cabinet($"utilisateurs/{user}"), new SetUserAccountRequest(), ContractsJsonContext.Default.SetUserAccountRequest))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        return user;
    }

    private static async Task<GroupSummary> CreateGroupAsync(HttpClient admin, string name)
    {
        HttpResponseMessage response = await admin.PostAsJsonAsync(
            Cabinet("groupes"), new CreateGroupRequest { Name = name }, ContractsJsonContext.Default.CreateGroupRequest);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.GroupSummary))!;
    }

    private static Task<HttpResponseMessage> AssignAsync(HttpClient client, AssignRoleRequest request) =>
        client.PostAsJsonAsync(Cabinet("attributions"), request, ContractsJsonContext.Default.AssignRoleRequest);

    private async Task<IReadOnlyList<Guid>> MyDossiersAsync(Guid user)
    {
        using HttpClient client = factory.ClientFor(user);
        HttpResponseMessage response = await client.GetAsync(MyDossiers);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        IReadOnlyList<DossierSummary>? dossiers = await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.IReadOnlyListDossierSummary);
        return dossiers!.Select(d => d.Id).ToArray();
    }

    /// <summary>Un dossier fermé à l'utilisateur répond 404 : l'API ne dit pas qu'il existe.</summary>
    private async Task<HttpStatusCode> OpenCustomersAsync(Guid user)
    {
        using HttpClient client = factory.ClientFor(user);
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return (await client.GetAsync(new Uri("/api/v1/customers", UriKind.Relative))).StatusCode;
    }

    [Fact]
    public async Task Un_role_sur_un_groupe_ouvre_ses_dossiers_et_son_retrait_les_referme_aussitot()
    {
        using HttpClient admin = Admin();
        Guid user = await NewAccountAsync(admin);
        GroupSummary group = await CreateGroupAsync(admin, "Pôle social");

        HttpResponseMessage assigned = await AssignAsync(admin, new AssignRoleRequest
        {
            SubjectKind = SubjectKindDto.User,
            SubjectId = user,
            Role = "collaborateur",
            ScopeKind = ScopeKindDto.Groupe,
            ScopeId = group.Id,
        });
        assigned.StatusCode.Should().Be(HttpStatusCode.Created);
        RoleAssignmentSummary assignment = (await assigned.Content.ReadFromJsonAsync(ContractsJsonContext.Default.RoleAssignmentSummary))!;

        // Groupe vide : rien à ouvrir. Une première lecture met les droits en cache.
        (await MyDossiersAsync(user)).Should().BeEmpty();
        (await OpenCustomersAsync(user)).Should().Be(HttpStatusCode.NotFound);

        (await admin.PutAsync(Group(group.Id, $"dossiers/{LettraApiFactory.DossierId}"), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await MyDossiersAsync(user)).Should().Equal(LettraApiFactory.DossierId);
        (await OpenCustomersAsync(user)).Should().Be(HttpStatusCode.OK);

        (await admin.DeleteAsync(Cabinet($"attributions/{assignment.Id}"))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await MyDossiersAsync(user)).Should().BeEmpty();
        (await OpenCustomersAsync(user)).Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Un_membre_herite_du_role_de_son_groupe_jusqu_a_son_retrait()
    {
        using HttpClient admin = Admin();
        Guid user = await NewAccountAsync(admin);
        GroupSummary group = await CreateGroupAsync(admin, "Pôle paie");
        (await admin.PutAsync(Group(group.Id, $"dossiers/{LettraApiFactory.DossierId}"), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await AssignAsync(admin, new AssignRoleRequest
        {
            SubjectKind = SubjectKindDto.Group,
            SubjectId = group.Id,
            Role = "collaborateur",
            ScopeKind = ScopeKindDto.Groupe,
            ScopeId = group.Id,
        })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await OpenCustomersAsync(user)).Should().Be(HttpStatusCode.NotFound);

        (await admin.PutAsync(Group(group.Id, $"membres/{user}"), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await OpenCustomersAsync(user)).Should().Be(HttpStatusCode.OK);

        (await admin.DeleteAsync(Group(group.Id, $"membres/{user}"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await OpenCustomersAsync(user)).Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Un_compte_suspendu_perd_ses_droits_aussitot()
    {
        using HttpClient admin = Admin();
        Guid user = await NewAccountAsync(admin);
        (await AssignAsync(admin, new AssignRoleRequest
        {
            SubjectId = user,
            Role = "collaborateur",
            ScopeKind = ScopeKindDto.Dossier,
            ScopeId = LettraApiFactory.DossierId,
        })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await OpenCustomersAsync(user)).Should().Be(HttpStatusCode.OK);

        (await admin.PutAsJsonAsync(
                Cabinet($"utilisateurs/{user}"),
                new SetUserAccountRequest { State = AccountStateDto.Suspended },
                ContractsJsonContext.Default.SetUserAccountRequest))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await OpenCustomersAsync(user)).Should().Be(HttpStatusCode.NotFound);
        (await MyDossiersAsync(user)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("collaborateur", ScopeKindDto.Cabinet, false, ErrorCodes.Rights.ScopeTooWide)]
    [InlineData("auditeur", ScopeKindDto.Dossier, false, ErrorCodes.Rights.ExpiryRequired)]
    [InlineData("administrateur-plateforme", ScopeKindDto.Cabinet, false, ErrorCodes.Rights.RoleUnknown)]
    [InlineData("role-inexistant", ScopeKindDto.Dossier, false, ErrorCodes.Rights.RoleUnknown)]
    [InlineData("collaborateur", ScopeKindDto.Dossier, true, ErrorCodes.Rights.ScopeNotFound)]
    public async Task Une_attribution_hors_des_regles_du_role_est_refusee(string role, ScopeKindDto scope, bool unknownScope, string code)
    {
        using HttpClient admin = Admin();
        Guid scopeId = unknownScope ? Guid.CreateVersion7()
            : scope == ScopeKindDto.Cabinet ? LettraApiFactory.CabinetId : LettraApiFactory.DossierId;

        HttpResponseMessage response = await AssignAsync(admin, new AssignRoleRequest
        {
            SubjectId = LettraApiFactory.CollaborateurId,
            Role = role,
            ScopeKind = scope,
            ScopeId = scopeId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(code);
    }

    [Fact]
    public async Task Un_cabinet_ne_touche_pas_aux_comptes_d_un_autre()
    {
        using HttpClient admin = Admin();

        (await admin.PutAsJsonAsync(
                Cabinet($"utilisateurs/{LettraApiFactory.AutreCabinetAdminId}"),
                new SetUserAccountRequest { State = AccountStateDto.Suspended },
                ContractsJsonContext.Default.SetUserAccountRequest))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AssignAsync(admin, new AssignRoleRequest
        {
            SubjectId = LettraApiFactory.AutreCabinetAdminId,
            Role = "collaborateur",
            ScopeKind = ScopeKindDto.Dossier,
            ScopeId = LettraApiFactory.DossierId,
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Et l'autre cabinet n'administre pas celui-ci.
        using HttpClient other = factory.ClientFor(LettraApiFactory.AutreCabinetAdminId);
        (await other.PostAsJsonAsync(Cabinet("groupes"), new CreateGroupRequest { Name = "Intrus" }, ContractsJsonContext.Default.CreateGroupRequest))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("manager")]
    [InlineData("collaborateur")]
    public async Task Sans_gerer_les_utilisateurs_on_n_attribue_rien(string who)
    {
        using HttpClient client = factory.ClientFor(who == "manager" ? LettraApiFactory.ManagerId : LettraApiFactory.CollaborateurId);

        (await AssignAsync(client, new AssignRoleRequest
        {
            SubjectId = who == "manager" ? LettraApiFactory.ManagerId : LettraApiFactory.CollaborateurId,
            Role = "administrateur-cabinet",
            ScopeKind = ScopeKindDto.Cabinet,
            ScopeId = LettraApiFactory.CabinetId,
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task La_liste_ne_montre_que_les_dossiers_lisibles()
    {
        (await MyDossiersAsync(LettraApiFactory.ManagerId)).Should().Contain(LettraApiFactory.DossierId);
        (await MyDossiersAsync(LettraApiFactory.CollaborateurId)).Should().Contain(LettraApiFactory.DossierId);
        (await MyDossiersAsync(LettraApiFactory.PortailId)).Should().BeEmpty();
        (await MyDossiersAsync(LettraApiFactory.AutreCabinetAdminId)).Should().BeEmpty();
    }
}
