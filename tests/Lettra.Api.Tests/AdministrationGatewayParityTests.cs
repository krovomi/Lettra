using Lettra.Contracts.Administration;
using Lettra.Contracts.Common;
using Lettra.Contracts.Rights;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Espace « Équipe et accès » par la gateway : en HTTP, sur le registre du Catalog de l'API (administrateur du cabinet) ;
/// en direct, sur le poste isolé, qui n'a pas de registre de cabinet.
/// </summary>
public sealed class AdministrationGatewayParityTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    private static readonly Guid Cabinet = LettraApiFactory.CabinetId;

    private readonly GatewayHarness _harness = new(factory);

    public static TheoryData<string> Transports => GatewayHarness.Transports;

    private ServiceProvider Admin() => _harness.HttpGateway(LettraApiFactory.DossierId, LettraApiFactory.AdministrateurCabinetId);

    [Fact]
    public async Task Une_personne_s_invite_se_modifie_s_active_recoit_un_role_sur_mesure_puis_le_rend()
    {
        await using ServiceProvider services = Admin();
        IAdministrationGateway admin = services.GetRequiredService<IAdministrationGateway>();
        string email = $"lea.{Guid.NewGuid():N}@cabinet-demo.example";

        PersonSummary lea = (await admin.InviteAsync(Cabinet, new InvitePersonRequest { DisplayName = "Léa Martin", Email = email }, default)).Value!;
        OperationResult<PersonSummary> again = await admin.InviteAsync(Cabinet, new InvitePersonRequest { DisplayName = "Léa M.", Email = email }, default);
        OperationResult<bool> renamed = await admin.UpdatePersonAsync(Cabinet, lea.Id, new UpdatePersonRequest { DisplayName = "Léa Martin-Roux", Email = email }, default);
        OperationResult<bool> activated = await admin.SetAccountStateAsync(Cabinet, lea.Id, AccountStateDto.Active, default);
        OperationResult<RoleDescription> role = await admin.SaveRoleAsync(Cabinet, null, new SaveCustomRoleRequest
        {
            Name = "Assistant devis",
            Areas = [new(AccessAreaDto.Quotes, AccessLevelDto.Work), new(AccessAreaDto.Parties, AccessLevelDto.View)],
        }, default);
        OperationResult<RoleDescription> edited = await admin.SaveRoleAsync(Cabinet, role.Value!.Key, new SaveCustomRoleRequest
        {
            Name = "Assistant devis et factures",
            Areas = [new(AccessAreaDto.Quotes, AccessLevelDto.Work), new(AccessAreaDto.Invoices, AccessLevelDto.View)],
        }, default);
        OperationResult<RoleAssignmentSummary> assigned = await admin.AssignAsync(Cabinet, new AssignRoleRequest
        {
            SubjectId = lea.Id, Role = role.Value.Key, ScopeKind = ScopeKindDto.Dossier, ScopeId = LettraApiFactory.DossierId,
        }, default);
        OperationResult<bool> inUse = await admin.DeleteRoleAsync(Cabinet, role.Value.Key, default);
        OperationResult<bool> revoked = await admin.RevokeAsync(Cabinet, assigned.Value!.Id, default);
        OperationResult<bool> roleDeleted = await admin.DeleteRoleAsync(Cabinet, role.Value.Key, default);
        AdministrationOverview overview = (await admin.GetOverviewAsync(Cabinet, default)).Value!;

        lea.State.Should().Be(AccountStateDto.Invited);
        again.Errors.Select(e => e.Code).Should().Contain(ErrorCodes.Administration.EmailTaken);
        renamed.Value.Should().BeTrue();
        activated.Value.Should().BeTrue();
        role.Value.Key.Should().StartWith("c-");
        edited.Value!.Name.Should().Be("Assistant devis et factures");
        inUse.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Administration.RoleInUse);
        revoked.Value.Should().BeTrue();
        roleDeleted.Value.Should().BeTrue();
        overview.People.Single(p => p.Id == lea.Id).Should().Match<PersonSummary>(p => p.DisplayName == "Léa Martin-Roux" && p.State == AccountStateDto.Active);
        overview.Roles.Should().NotContain(r => r.Key == role.Value.Key);
    }

    [Fact]
    public async Task Une_equipe_se_cree_recoit_un_membre_et_un_dossier_se_renomme_puis_se_supprime()
    {
        await using ServiceProvider services = Admin();
        IAdministrationGateway admin = services.GetRequiredService<IAdministrationGateway>();

        GroupSummary team = (await admin.CreateTeamAsync(Cabinet, new CreateGroupRequest { Name = "Pôle TPE" }, default)).Value!;
        OperationResult<bool> member = await admin.SetTeamMemberAsync(team.Id, LettraApiFactory.CollaborateurId, member: true, default);
        OperationResult<bool> placed = await admin.SetTeamDossierAsync(team.Id, LettraApiFactory.DossierId, placed: true, default);
        OperationResult<bool> renamed = await admin.RenameTeamAsync(Cabinet, team.Id, new RenameTeamRequest { Name = "Pôle commerçants" }, default);
        TeamSummary during = (await admin.GetOverviewAsync(Cabinet, default)).Value!.Teams.Single(t => t.Id == team.Id);
        OperationResult<bool> left = await admin.SetTeamMemberAsync(team.Id, LettraApiFactory.CollaborateurId, member: false, default);
        OperationResult<bool> removed = await admin.SetTeamDossierAsync(team.Id, LettraApiFactory.DossierId, placed: false, default);
        OperationResult<bool> deleted = await admin.DeleteTeamAsync(Cabinet, team.Id, default);

        member.Value.Should().BeTrue();
        placed.Value.Should().BeTrue();
        renamed.Value.Should().BeTrue();
        during.Name.Should().Be("Pôle commerçants");
        left.Value.Should().BeTrue();
        removed.Value.Should().BeTrue();
        deleted.Value.Should().BeTrue();
        (await admin.GetOverviewAsync(Cabinet, default)).Value!.Teams.Should().NotContain(t => t.Id == team.Id);
    }

    [Fact]
    public async Task Les_garde_fous_et_les_refus_passent_par_la_gateway_avec_leur_code()
    {
        await using ServiceProvider services = Admin();
        IAdministrationGateway admin = services.GetRequiredService<IAdministrationGateway>();
        PersonSummary me = (await admin.GetOverviewAsync(Cabinet, default)).Value!.People.Single(p => p.IsMe);

        OperationResult<bool> suspendMe = await admin.SetAccountStateAsync(Cabinet, me.Id, AccountStateDto.Suspended, default);
        OperationResult<bool> revokeMine = await admin.RevokeAsync(Cabinet, me.Assignments.Single(a => a.Role == "administrateur-cabinet").Id, default);
        OperationResult<RoleDescription> empty = await admin.SaveRoleAsync(Cabinet, null, new SaveCustomRoleRequest { Name = "Vide", Areas = [] }, default);
        OperationResult<PersonSummary> unnamed = await admin.InviteAsync(Cabinet, new InvitePersonRequest { DisplayName = "", Email = "a@b.fr" }, default);

        suspendMe.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Administration.SelfLockout);
        revokeMine.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Administration.SelfLockout);
        empty.Errors.Select(e => e.Code).Should().Contain(ErrorCodes.Administration.RoleEmpty);
        unnamed.Errors.Select(e => e.Code).Should().Contain(ErrorCodes.Administration.NameRequired);
    }

    [Fact]
    public async Task Sans_droit_d_administration_l_espace_reste_ferme_mais_chacun_lit_ses_acces()
    {
        await using ServiceProvider services = _harness.HttpGateway(LettraApiFactory.DossierId, LettraApiFactory.CollaborateurId);
        IAdministrationGateway gateway = services.GetRequiredService<IAdministrationGateway>();

        OperationResult<AdministrationOverview> overview = await gateway.GetOverviewAsync(Cabinet, default);
        OperationResult<PersonSummary> invite = await gateway.InviteAsync(Cabinet, new InvitePersonRequest { DisplayName = "Intrus", Email = "intrus@exemple.fr" }, default);
        OperationResult<bool> rename = await gateway.RenameTeamAsync(Cabinet, Guid.CreateVersion7(), new RenameTeamRequest { Name = "Intrus" }, default);
        OperationResult<MyAccessResponse> access = await gateway.GetMyAccessAsync(LettraApiFactory.DossierId, default);
        OperationResult<MyAccessResponse> anywhere = await gateway.GetMyAccessAsync(null, default);

        overview.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Security.Forbidden);
        invite.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Security.Forbidden);
        rename.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Security.Forbidden);
        access.Value!.CanAdminister.Should().BeFalse();
        access.Value.LevelOf(AccessAreaDto.Quotes).Should().Be(AccessLevelDto.Work);
        anywhere.IsSuccess.Should().BeTrue(GatewayHarness.Codes(anywhere));
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Chaque_transport_lit_l_espace_de_son_cabinet_et_le_poste_isole_ne_l_ecrit_pas(string transport)
    {
        await using ServiceProvider services = transport == "http" ? Admin() : _harness.InProcessGateway(null);
        IAdministrationGateway admin = services.GetRequiredService<IAdministrationGateway>();

        OperationResult<MyAccessResponse> access = await admin.GetMyAccessAsync(null, default);
        access.IsSuccess.Should().BeTrue(GatewayHarness.Codes(access));
        Guid cabinet = access.Value!.CabinetId!.Value;
        OperationResult<AdministrationOverview> overview = await admin.GetOverviewAsync(cabinet, default);
        OperationResult<GroupSummary> team = await admin.CreateTeamAsync(cabinet, new CreateGroupRequest { Name = "Équipe " + transport }, default);
        OperationResult<AdministrationOverview> other = await admin.GetOverviewAsync(LettraApiFactory.OtherCabinetId, default);

        access.Value.CanAdminister.Should().BeTrue();
        other.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Security.Forbidden, "un autre cabinet reste fermé, sur les deux transports");
        if (transport == "http")
        {
            cabinet.Should().Be(Cabinet);
            overview.Value!.Can.Should().Be(new AdministrationCapabilities(ManagePeople: true, ManageTeams: true, ManageRoles: true, ReadOnly: false));
            team.IsSuccess.Should().BeTrue(GatewayHarness.Codes(team));
            (await admin.DeleteTeamAsync(cabinet, team.Value!.Id, default)).Value.Should().BeTrue();
        }
        else
        {
            // Écart voulu : le poste isolé lit les droits de son registre local, en lecture seule ; toute écriture est
            // refusée par Rights.NotSupported (pas de registre de cabinet à administrer).
            overview.Value!.Can.Should().Be(new AdministrationCapabilities(ManagePeople: false, ManageTeams: false, ManageRoles: false, ReadOnly: true));
            team.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Rights.NotSupported);
        }
    }

    [Fact]
    public async Task Le_poste_isole_refuse_chaque_ecriture_d_administration()
    {
        await using ServiceProvider services = _harness.InProcessGateway(null);
        IAdministrationGateway admin = services.GetRequiredService<IAdministrationGateway>();
        MyAccessResponse access = (await admin.GetMyAccessAsync(null, default)).Value!;
        Guid cabinet = access.CabinetId!.Value;
        Guid id = Guid.CreateVersion7();
        (await admin.GetMyAccessAsync(id, default)).IsSuccess.Should().BeTrue();

        List<IReadOnlyList<ErrorDetail>> refused =
        [
            (await admin.InviteAsync(cabinet, new InvitePersonRequest { DisplayName = "Léa Martin", Email = "lea@cabinet-demo.example" }, default)).Errors,
            (await admin.UpdatePersonAsync(cabinet, id, new UpdatePersonRequest { DisplayName = "Léa Martin", Email = "lea@cabinet-demo.example" }, default)).Errors,
            (await admin.SetAccountStateAsync(cabinet, id, AccountStateDto.Suspended, default)).Errors,
            (await admin.RevokeAsync(cabinet, id, default)).Errors,
            (await admin.RenameTeamAsync(cabinet, id, new RenameTeamRequest { Name = "Pôle" }, default)).Errors,
            (await admin.DeleteTeamAsync(cabinet, id, default)).Errors,
            (await admin.SaveRoleAsync(cabinet, null, new SaveCustomRoleRequest { Name = "Rôle", Areas = [new(AccessAreaDto.Quotes, AccessLevelDto.View)] }, default)).Errors,
            (await admin.DeleteRoleAsync(cabinet, "c-role", default)).Errors,
        ];
        OperationResult<RoleAssignmentSummary> cabinetWide = await admin.AssignAsync(
            cabinet, new AssignRoleRequest { SubjectId = id, Role = "collaborateur", ScopeKind = ScopeKindDto.Cabinet, ScopeId = cabinet }, default);
        OperationResult<bool> member = await admin.SetTeamMemberAsync(id, id, member: true, default);
        OperationResult<bool> placed = await admin.SetTeamDossierAsync(id, id, placed: true, default);
        OperationResult<RoleDescription> noAreas = await admin.SaveRoleAsync(cabinet, "c-role", new SaveCustomRoleRequest { Name = "Rôle", Areas = null! }, default);

        refused.Should().AllSatisfy(errors => errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Rights.NotSupported));

        // Les contrôles qui passent avant le registre gardent leur propre code, comme sur le serveur.
        cabinetWide.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Rights.ScopeTooWide);
        member.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Security.Forbidden, "une équipe inconnue n'est d'aucun cabinet");
        placed.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Security.Forbidden);
        noAreas.Errors.Select(e => e.Code).Should().Contain(ErrorCodes.Administration.RoleEmpty);
    }

    [Fact]
    public async Task Serveur_injoignable_chaque_appel_rend_Api_Unreachable()
    {
        await using ServiceProvider services = GatewayHarness.Unreachable();
        IAdministrationGateway admin = services.GetRequiredService<IAdministrationGateway>();
        Guid id = Guid.CreateVersion7();

        List<IReadOnlyList<ErrorDetail>> failures =
        [
            (await admin.GetMyAccessAsync(id, default)).Errors,
            (await admin.GetMyAccessAsync(null, default)).Errors,
            (await admin.GetOverviewAsync(id, default)).Errors,
            (await admin.InviteAsync(id, new InvitePersonRequest(), default)).Errors,
            (await admin.UpdatePersonAsync(id, id, new UpdatePersonRequest(), default)).Errors,
            (await admin.SetAccountStateAsync(id, id, AccountStateDto.Active, default)).Errors,
            (await admin.AssignAsync(id, new AssignRoleRequest(), default)).Errors,
            (await admin.RevokeAsync(id, id, default)).Errors,
            (await admin.CreateTeamAsync(id, new CreateGroupRequest(), default)).Errors,
            (await admin.RenameTeamAsync(id, id, new RenameTeamRequest(), default)).Errors,
            (await admin.DeleteTeamAsync(id, id, default)).Errors,
            (await admin.SetTeamMemberAsync(id, id, member: true, default)).Errors,
            (await admin.SetTeamMemberAsync(id, id, member: false, default)).Errors,
            (await admin.SetTeamDossierAsync(id, id, placed: true, default)).Errors,
            (await admin.SetTeamDossierAsync(id, id, placed: false, default)).Errors,
            (await admin.SaveRoleAsync(id, null, new SaveCustomRoleRequest(), default)).Errors,
            (await admin.SaveRoleAsync(id, "c-role", new SaveCustomRoleRequest(), default)).Errors,
            (await admin.DeleteRoleAsync(id, "c-role", default)).Errors,
        ];

        failures.Should().AllSatisfy(errors => errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unreachable));
    }

    public void Dispose() => _harness.Dispose();
}
