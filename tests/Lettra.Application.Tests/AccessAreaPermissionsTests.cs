using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Authorization;
using Lettra.Contracts.Administration;

namespace Lettra.Application.Tests;

/// <summary>
/// Le pont entre les pages que voit l'administrateur et les permissions que vérifie le serveur : un rôle décrit en pages
/// ne donne jamais plus que ce qu'il affiche, et un rôle sur mesure ne peut pas toucher à l'administration.
/// </summary>
public sealed class AccessAreaPermissionsTests
{
    public static TheoryData<string> Templates() => [.. RoleTemplates.All.Keys];

    /// <summary>
    /// « Partir de ce rôle » : ce que l'éditeur affiche est exactement ce que le rôle sur mesure donnera. Un rôle fourni
    /// peut n'avoir qu'une partie d'un niveau (la délégation exporte le FEC sans vérifier l'intégrité) ; l'écran montre
    /// alors le geste le plus haut, et la copie reçoit le niveau entier, visible avant d'enregistrer.
    /// </summary>
    [Theory]
    [MemberData(nameof(Templates))]
    public void Ce_que_l_editeur_affiche_est_ce_que_le_role_sur_mesure_donne(string key)
    {
        IReadOnlyList<AreaAccess> shown = [.. AccessAreaPermissions.Describe(RoleTemplates.All[key].Permissions)
            .Where(p => AccessAreas.Of(p.Area).Customizable)];

        IReadOnlySet<Permission>? composed = AccessAreaPermissions.Compose(shown);

        // Devis, factures et avoirs se consultent ensemble : l'éditeur affiche déjà « Voir » sur les deux autres.
        IReadOnlyList<AreaAccess> expected = shown.Any(p => AccessAreas.Of(p.Area).SharesSalesView)
            ? [.. shown, .. AccessAreas.All.Where(a => a.SharesSalesView && shown.All(p => p.Area != a.Area)).Select(a => new AreaAccess(a.Area, AccessLevelDto.View))]
            : shown;
        composed.Should().NotBeNull();
        AccessAreaPermissions.Describe(composed!).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Le_collaborateur_se_lit_en_mots_simples()
    {
        IReadOnlyList<AreaAccess> pages = AccessAreaPermissions.Describe(RoleTemplates.All[RoleTemplates.Collaborateur].Permissions);

        pages.Should().Contain(new AreaAccess(AccessAreaDto.Quotes, AccessLevelDto.Work));
        pages.Should().NotContain(p => p.Area == AccessAreaDto.Administration);
        pages.Should().NotContain(p => p.Level == AccessLevelDto.Approve && p.Area == AccessAreaDto.Invoices, "le collaborateur ne valide pas");
    }

    [Fact]
    public void L_administrateur_du_cabinet_administre_au_plus_haut_niveau()
    {
        IReadOnlyList<AreaAccess> pages = AccessAreaPermissions.Describe(RoleTemplates.All[RoleTemplates.AdministrateurCabinet].Permissions);

        pages.Should().Contain(new AreaAccess(AccessAreaDto.Administration, AccessLevelDto.Approve));
    }

    [Theory]
    [InlineData(AccessAreaDto.Administration, AccessLevelDto.View)]
    [InlineData(AccessAreaDto.Portal, AccessLevelDto.View)]
    [InlineData(AccessAreaDto.Quotes, AccessLevelDto.Approve)]
    [InlineData(AccessAreaDto.Assistant, AccessLevelDto.View)]
    public void Un_role_sur_mesure_refuse_une_page_ou_un_niveau_hors_de_portee(AccessAreaDto area, AccessLevelDto level) =>
        AccessAreaPermissions.Compose([new AreaAccess(area, level)]).Should().BeNull();

    [Fact]
    public void Chaque_niveau_comprend_les_niveaux_inferieurs()
    {
        IReadOnlySet<Permission> view = AccessAreaPermissions.Compose([new AreaAccess(AccessAreaDto.Invoices, AccessLevelDto.View)])!;
        IReadOnlySet<Permission> work = AccessAreaPermissions.Compose([new AreaAccess(AccessAreaDto.Invoices, AccessLevelDto.Work)])!;
        IReadOnlySet<Permission> approve = AccessAreaPermissions.Compose([new AreaAccess(AccessAreaDto.Invoices, AccessLevelDto.Approve)])!;

        view.Should().BeSubsetOf(work);
        work.Should().BeSubsetOf(approve);
        approve.Should().Contain(Permission.FactureValider);
        work.Should().NotContain(Permission.FactureValider);
        AccessAreaPermissions.Describe(approve).Should().Contain(new AreaAccess(AccessAreaDto.Invoices, AccessLevelDto.Approve));
    }

    [Fact]
    public void Aucune_page_ne_donne_aucun_droit() =>
        AccessAreaPermissions.Describe(new HashSet<Permission>()).Should().BeEmpty();
}
