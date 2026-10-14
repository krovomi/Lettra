using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Authorization;
using Lettra.Domain.SharedKernel;

namespace Lettra.Identity.Tests;

/// <summary>Règles du calcul des droits : refus par défaut, échéances, groupes, portées, rôles du cabinet.</summary>
public sealed class PermissionResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly OrganisationId Cabinet = new(Guid.CreateVersion7());
    private static readonly GroupId Group = new(Guid.CreateVersion7());
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());
    private static readonly Guid User = Guid.CreateVersion7();
    private static readonly ScopePath Target = PermissionMatrixTests.DossierPath(Dossier, Cabinet, Group);

    private static UserAuthorizationProfile Profile(params RoleAssignment[] assignments) =>
        new(User, AccountState.Active, Cabinet, [Group], assignments, []);

    private static RoleAssignment Assign(string role, AuthorizationScope scope, DateTimeOffset? expires = null, SubjectRef? subject = null) =>
        new(subject ?? new SubjectRef(SubjectKind.User, User), role, scope, expires);

    [Fact]
    public void Sans_attribution_rien() =>
        PermissionResolver.Resolve(Profile(), Target, Now).Should().BeEmpty();

    [Theory]
    [InlineData(AccountState.Invited)]
    [InlineData(AccountState.Suspended)]
    [InlineData(AccountState.Locked)]
    [InlineData(AccountState.Disabled)]
    [InlineData(AccountState.Deleted)]
    public void Seul_un_compte_actif_a_des_droits(AccountState state) =>
        PermissionResolver.Resolve(
            Profile(Assign(RoleTemplates.Manager, AuthorizationScope.ForCabinet(Cabinet))) with { State = state }, Target, Now)
            .Should().BeEmpty();

    [Fact]
    public void Un_auditeur_sans_echeance_n_a_rien() =>
        PermissionResolver.Resolve(Profile(Assign(RoleTemplates.Auditeur, AuthorizationScope.ForDossier(Dossier))), Target, Now)
            .Should().BeEmpty();

    [Fact]
    public void Un_auditeur_avec_echeance_lit_jusqu_a_l_echeance()
    {
        UserAuthorizationProfile profile = Profile(Assign(RoleTemplates.Auditeur, AuthorizationScope.ForDossier(Dossier), Now.AddHours(1)));

        PermissionResolver.Resolve(profile, Target, Now).Should().Contain(Permission.DossierLire);
        PermissionResolver.Resolve(profile, Target, Now.AddHours(1)).Should().BeEmpty("l'échéance est exclue");
    }

    [Fact]
    public void Un_role_n_est_jamais_attribue_plus_largement_que_prevu() =>
        PermissionResolver.Resolve(Profile(Assign(RoleTemplates.Collaborateur, AuthorizationScope.ForCabinet(Cabinet))), Target, Now)
            .Should().BeEmpty("un collaborateur s'attribue sur un groupe ou un dossier, pas sur tout le cabinet");

    [Fact]
    public void Une_attribution_au_groupe_vaut_pour_ses_membres()
    {
        UserAuthorizationProfile profile = Profile(Assign(RoleTemplates.Collaborateur, AuthorizationScope.ForDossier(Dossier), subject: new SubjectRef(SubjectKind.Group, Group.Value)));

        PermissionResolver.Resolve(profile, Target, Now).Should().Contain(Permission.FactureSaisir);
        PermissionResolver.Resolve(profile with { Groups = [] }, Target, Now).Should().BeEmpty("il a quitté le groupe");
    }

    [Fact]
    public void Une_attribution_d_un_autre_sujet_est_ignoree() =>
        PermissionResolver.Resolve(
            Profile(Assign(RoleTemplates.Manager, AuthorizationScope.ForCabinet(Cabinet), subject: new SubjectRef(SubjectKind.User, Guid.CreateVersion7()))), Target, Now)
            .Should().BeEmpty();

    [Fact]
    public void Les_attributions_se_cumulent()
    {
        UserAuthorizationProfile profile = Profile(
            Assign(RoleTemplates.CollaborateurEtendu, AuthorizationScope.ForGroup(Group)),
            Assign(RoleTemplates.DelegationAvoirsExports, AuthorizationScope.ForGroup(Group), Now.AddDays(14)));

        PermissionResolver.Resolve(profile, Target, Now).Should().Contain([Permission.FactureEmettre, Permission.AvoirCreer, Permission.FecExporter]);
    }

    [Fact]
    public void Un_role_du_cabinet_compose_des_permissions_existantes()
    {
        RoleDefinition saisie = new("saisie-seule", new HashSet<Permission> { Permission.DossierLire, Permission.FactureSaisir }, ScopeKind.Dossier);
        UserAuthorizationProfile profile = Profile(Assign("saisie-seule", AuthorizationScope.ForDossier(Dossier))) with { CustomRoles = [saisie] };

        PermissionResolver.Resolve(profile, Target, Now).Should().BeEquivalentTo([Permission.DossierLire, Permission.FactureSaisir]);
    }

    [Fact]
    public void Un_role_fourni_ne_peut_pas_etre_redefini_par_un_cabinet()
    {
        RoleDefinition piege = new(RoleTemplates.Collaborateur, new HashSet<Permission>(Enum.GetValues<Permission>()), ScopeKind.Cabinet);
        UserAuthorizationProfile profile = Profile(Assign(RoleTemplates.Collaborateur, AuthorizationScope.ForDossier(Dossier))) with { CustomRoles = [piege] };

        PermissionResolver.Resolve(profile, Target, Now).Should().NotContain(Permission.FactureEmettre);
    }

    [Fact]
    public void Un_role_inconnu_ne_donne_rien() =>
        PermissionResolver.Resolve(Profile(Assign("inconnu", AuthorizationScope.ForDossier(Dossier))), Target, Now).Should().BeEmpty();

    [Fact]
    public void Un_utilisateur_sans_cabinet_n_a_rien_hors_plateforme() =>
        PermissionResolver.Resolve(Profile(Assign(RoleTemplates.Manager, AuthorizationScope.ForCabinet(Cabinet))) with { Cabinet = null }, Target, Now)
            .Should().BeEmpty();

    [Fact]
    public void Une_portee_inconnue_ne_donne_rien() =>
        PermissionResolver.Resolve(Profile(Assign(RoleTemplates.Manager, AuthorizationScope.ForCabinet(Cabinet))), null, Now).Should().BeEmpty();
}
