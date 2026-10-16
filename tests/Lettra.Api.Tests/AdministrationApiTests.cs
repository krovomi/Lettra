using System.Net;
using System.Net.Http.Json;
using Lettra.Contracts.Administration;
using Lettra.Contracts.Common;
using Lettra.Contracts.Rights;
using Lettra.Contracts.Serialization;

namespace Lettra.Api.Tests;

/// <summary>
/// Espace « Équipe et accès » de bout en bout, sur le registre du Catalog : une lecture qui dit tout à l'écran, des
/// personnes nommées, des rôles sur mesure qui ouvrent vraiment les pages choisies, et les garde-fous (adresse en double,
/// rôle encore donné, administrateur qui se retirerait ses propres droits).
/// </summary>
public sealed class AdministrationApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static Uri Cabinet(string path) => new($"/api/v1/cabinets/{LettraApiFactory.CabinetId}/{path}", UriKind.Relative);

    private HttpClient Admin() => factory.ClientFor(LettraApiFactory.AdministrateurCabinetId);

    private static async Task<AdministrationOverview> OverviewAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.GetAsync(Cabinet("administration"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.AdministrationOverview))!;
    }

    private static async Task<PersonSummary> InviteAsync(HttpClient admin, string name, string email)
    {
        HttpResponseMessage response = await admin.PostAsJsonAsync(
            Cabinet("personnes"), new InvitePersonRequest { DisplayName = name, Email = email }, ContractsJsonContext.Default.InvitePersonRequest);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.PersonSummary))!;
    }

    private async Task<MyAccessResponse> MyAccessAsync(Guid user)
    {
        using HttpClient client = factory.ClientFor(user);
        HttpResponseMessage response = await client.GetAsync(new Uri($"/api/v1/dossiers/acces?dossier={LettraApiFactory.DossierId}", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.MyAccessResponse))!;
    }

    [Fact]
    public async Task L_administrateur_lit_tout_l_espace_en_une_fois()
    {
        using HttpClient admin = Admin();

        AdministrationOverview overview = await OverviewAsync(admin);

        overview.CabinetId.Should().Be(LettraApiFactory.CabinetId);
        overview.Can.Should().Be(new AdministrationCapabilities(ManagePeople: true, ManageTeams: true, ManageRoles: true, ReadOnly: false));
        overview.People.Should().Contain(p => p.Id == LettraApiFactory.AdministrateurCabinetId && p.IsMe);
        overview.People.Should().Contain(p => p.Id == LettraApiFactory.CollaborateurId);
        overview.People.Should().NotContain(p => p.Id == LettraApiFactory.AutreCabinetAdminId, "un autre cabinet reste invisible");
        overview.Roles.Select(r => r.Key).Should().Contain(["administrateur-cabinet", "manager", "collaborateur", "auditeur"])
            .And.NotContain("administrateur-plateforme");
        overview.Roles.Single(r => r.Key == "collaborateur").Areas.Should().Contain(new AreaAccess(AccessAreaDto.Quotes, AccessLevelDto.Work));
        overview.Dossiers.Should().Contain(d => d.Id == LettraApiFactory.DossierId);
    }

    [Theory]
    [InlineData("collaborateur")]
    [InlineData("portail")]
    public async Task Sans_droit_d_administration_l_espace_est_ferme(string who)
    {
        using HttpClient client = factory.ClientFor(who == "portail" ? LettraApiFactory.PortailId : LettraApiFactory.CollaborateurId);

        (await client.GetAsync(Cabinet("administration"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync(Cabinet("personnes"), new InvitePersonRequest { DisplayName = "Intrus", Email = "intrus@exemple.fr" }, ContractsJsonContext.Default.InvitePersonRequest))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Une_personne_ajoutee_apparait_nommee_et_invitee_et_son_adresse_ne_sert_qu_une_fois()
    {
        using HttpClient admin = Admin();
        string email = $"lea.{Guid.NewGuid():N}@cabinet-demo.example";

        PersonSummary lea = await InviteAsync(admin, "  Léa Martin ", email.ToUpperInvariant());

        lea.DisplayName.Should().Be("Léa Martin");
        lea.Email.Should().Be(email, "les adresses sont rangées en minuscules");
        lea.State.Should().Be(AccountStateDto.Invited);
        (await OverviewAsync(admin)).People.Should().ContainSingle(p => p.Id == lea.Id);

        HttpResponseMessage again = await admin.PostAsJsonAsync(
            Cabinet("personnes"), new InvitePersonRequest { DisplayName = "Léa M.", Email = email }, ContractsJsonContext.Default.InvitePersonRequest);
        again.StatusCode.Should().Be(HttpStatusCode.BadRequest, "le message s'affiche sous le champ adresse");
        (await again.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Administration.EmailTaken);

        (await admin.PutAsJsonAsync(Cabinet($"personnes/{lea.Id}"), new UpdatePersonRequest { DisplayName = "Léa Martin-Roux", Email = email }, ContractsJsonContext.Default.UpdatePersonRequest))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await OverviewAsync(admin)).People.Single(p => p.Id == lea.Id).DisplayName.Should().Be("Léa Martin-Roux");
    }

    [Theory]
    [InlineData("", "a@b.fr", ErrorCodes.Administration.NameRequired)]
    [InlineData("Léa", "pas-une-adresse", ErrorCodes.Administration.EmailInvalid)]
    [InlineData("Léa", "lea@localhost", ErrorCodes.Administration.EmailInvalid)]
    public async Task Une_fiche_incomplete_est_refusee_avec_un_message_clair(string name, string email, string code)
    {
        using HttpClient admin = Admin();

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            Cabinet("personnes"), new InvitePersonRequest { DisplayName = name, Email = email }, ContractsJsonContext.Default.InvitePersonRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(code);
    }

    [Fact]
    public async Task Un_role_sur_mesure_ouvre_exactement_les_pages_choisies_puis_se_supprime_une_fois_rendu()
    {
        using HttpClient admin = Admin();
        PersonSummary person = await InviteAsync(admin, "Hugo Lefèvre", $"hugo.{Guid.NewGuid():N}@cabinet-demo.example");
        (await admin.PutAsJsonAsync(Cabinet($"utilisateurs/{person.Id}"), new SetUserAccountRequest { State = AccountStateDto.Active }, ContractsJsonContext.Default.SetUserAccountRequest))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage created = await admin.PostAsJsonAsync(Cabinet("roles"), new SaveCustomRoleRequest
        {
            Name = "Assistant devis",
            Description = "Prépare les devis, consulte les clients.",
            Areas = [new(AccessAreaDto.Quotes, AccessLevelDto.Work), new(AccessAreaDto.Parties, AccessLevelDto.View)],
        }, ContractsJsonContext.Default.SaveCustomRoleRequest);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        RoleDescription role = (await created.Content.ReadFromJsonAsync(ContractsJsonContext.Default.RoleDescription))!;
        role.IsCustom.Should().BeTrue();
        role.Key.Should().StartWith("c-");

        HttpResponseMessage assigned = await admin.PostAsJsonAsync(Cabinet("attributions"), new AssignRoleRequest
        {
            SubjectId = person.Id,
            Role = role.Key,
            ScopeKind = ScopeKindDto.Dossier,
            ScopeId = LettraApiFactory.DossierId,
        }, ContractsJsonContext.Default.AssignRoleRequest);
        assigned.StatusCode.Should().Be(HttpStatusCode.Created);
        RoleAssignmentSummary assignment = (await assigned.Content.ReadFromJsonAsync(ContractsJsonContext.Default.RoleAssignmentSummary))!;

        MyAccessResponse access = await MyAccessAsync(person.Id);
        access.CanAdminister.Should().BeFalse();
        access.LevelOf(AccessAreaDto.Quotes).Should().Be(AccessLevelDto.Work);
        access.LevelOf(AccessAreaDto.Parties).Should().Be(AccessLevelDto.View);
        access.LevelOf(AccessAreaDto.Invoices).Should().Be(AccessLevelDto.View, "devis, factures et avoirs se consultent ensemble");
        access.LevelOf(AccessAreaDto.Accounting).Should().Be(AccessLevelDto.None);

        HttpResponseMessage inUse = await admin.DeleteAsync(Cabinet($"roles/{role.Key}"));
        inUse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await inUse.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Administration.RoleInUse);

        (await admin.DeleteAsync(Cabinet($"attributions/{assignment.Id}"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.DeleteAsync(Cabinet($"roles/{role.Key}"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await OverviewAsync(admin)).Roles.Should().NotContain(r => r.Key == role.Key);
    }

    [Fact]
    public async Task Un_role_sur_mesure_vide_ou_qui_donnerait_l_administration_est_refuse()
    {
        using HttpClient admin = Admin();

        HttpResponseMessage empty = await admin.PostAsJsonAsync(Cabinet("roles"), new SaveCustomRoleRequest { Name = "Vide", Areas = [] }, ContractsJsonContext.Default.SaveCustomRoleRequest);
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await empty.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Administration.RoleEmpty);

        HttpResponseMessage admins = await admin.PostAsJsonAsync(Cabinet("roles"), new SaveCustomRoleRequest
        {
            Name = "Presque admin",
            Areas = [new(AccessAreaDto.Administration, AccessLevelDto.Approve)],
        }, ContractsJsonContext.Default.SaveCustomRoleRequest);
        admins.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admins.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Administration.RoleAreaInvalid);
    }

    [Fact]
    public async Task Une_equipe_se_renomme_puis_se_supprime_avec_ses_acces()
    {
        using HttpClient admin = Admin();
        HttpResponseMessage created = await admin.PostAsJsonAsync(Cabinet("groupes"), new CreateGroupRequest { Name = "Pôle TPE" }, ContractsJsonContext.Default.CreateGroupRequest);
        GroupSummary team = (await created.Content.ReadFromJsonAsync(ContractsJsonContext.Default.GroupSummary))!;
        (await admin.PostAsJsonAsync(Cabinet("attributions"), new AssignRoleRequest
        {
            SubjectKind = SubjectKindDto.Group,
            SubjectId = team.Id,
            Role = "collaborateur",
            ScopeKind = ScopeKindDto.Groupe,
            ScopeId = team.Id,
        }, ContractsJsonContext.Default.AssignRoleRequest)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await admin.PutAsJsonAsync(Cabinet($"groupes/{team.Id}"), new RenameTeamRequest { Name = "Pôle commerçants" }, ContractsJsonContext.Default.RenameTeamRequest))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        TeamSummary renamed = (await OverviewAsync(admin)).Teams.Single(t => t.Id == team.Id);
        renamed.Name.Should().Be("Pôle commerçants");
        renamed.Assignments.Should().ContainSingle(a => a.Role == "collaborateur");

        (await admin.DeleteAsync(Cabinet($"groupes/{team.Id}"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        AdministrationOverview after = await OverviewAsync(admin);
        after.Teams.Should().NotContain(t => t.Id == team.Id);
        after.Roles.Single(r => r.Key == "collaborateur").AssignedCount.Should().Be(
            after.People.Sum(p => p.Assignments.Count(a => a.Role == "collaborateur")) + after.Teams.Sum(t => t.Assignments.Count(a => a.Role == "collaborateur")),
            "les accès de l'équipe partent avec elle");
    }

    [Fact]
    public async Task L_administrateur_ne_peut_ni_se_suspendre_ni_se_retirer_son_dernier_acces_d_administration()
    {
        using HttpClient admin = Admin();
        PersonSummary me = (await OverviewAsync(admin)).People.Single(p => p.IsMe);
        RoleAssignmentSummary mine = me.Assignments.Single(a => a.Role == "administrateur-cabinet");

        HttpResponseMessage suspend = await admin.PutAsJsonAsync(
            Cabinet($"utilisateurs/{me.Id}"), new SetUserAccountRequest { State = AccountStateDto.Suspended }, ContractsJsonContext.Default.SetUserAccountRequest);
        suspend.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await suspend.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Administration.SelfLockout);

        HttpResponseMessage revoke = await admin.DeleteAsync(Cabinet($"attributions/{mine.Id}"));
        revoke.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await revoke.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Administration.SelfLockout);

        (await OverviewAsync(admin)).People.Single(p => p.IsMe).State.Should().Be(AccountStateDto.Active);
    }

    [Fact]
    public async Task Chacun_lit_ce_qu_il_peut_ouvrir()
    {
        MyAccessResponse admin = await MyAccessAsync(LettraApiFactory.AdministrateurCabinetId);
        admin.CanAdminister.Should().BeTrue();
        admin.CabinetId.Should().Be(LettraApiFactory.CabinetId);

        MyAccessResponse collaborateur = await MyAccessAsync(LettraApiFactory.CollaborateurId);
        collaborateur.CanAdminister.Should().BeFalse();
        collaborateur.LevelOf(AccessAreaDto.Quotes).Should().Be(AccessLevelDto.Work);
        collaborateur.LevelOf(AccessAreaDto.Invoices).Should().BeOneOf(AccessLevelDto.View, AccessLevelDto.Work);

        MyAccessResponse suspendu = await MyAccessAsync(LettraApiFactory.SuspenduId);
        suspendu.Should().BeEquivalentTo(new MyAccessResponse(null, false, []));
    }
}
