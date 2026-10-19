using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Authorization;

namespace Lettra.Application.Tests;

/// <summary>Les règles d'attribution d'un rôle, partagées par l'attribution et le calcul des droits.</summary>
public sealed class RoleDefinitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static RoleDefinition Role(ScopeKind widest, bool requiresExpiry = false) =>
        new("test", new HashSet<Permission>(), widest, requiresExpiry);

    [Theory]
    [InlineData(ScopeKind.Groupe, ScopeKind.Groupe, true)]
    [InlineData(ScopeKind.Groupe, ScopeKind.Dossier, true)]
    [InlineData(ScopeKind.Groupe, ScopeKind.Cabinet, false)]
    [InlineData(ScopeKind.Dossier, ScopeKind.Groupe, false)]
    public void Un_role_ne_se_donne_pas_plus_largement_que_prevu(ScopeKind widest, ScopeKind scope, bool allowed) =>
        Role(widest).AllowsScope(scope).Should().Be(allowed);

    [Fact]
    public void Sans_duree_limitee_l_echeance_est_facultative_mais_jamais_passee()
    {
        RoleDefinition role = Role(ScopeKind.Dossier);

        role.AcceptsExpiry(null, Now).Should().BeTrue();
        role.AcceptsExpiry(Now.AddDays(1), Now).Should().BeTrue();
        role.AcceptsExpiry(Now, Now).Should().BeFalse("une échéance atteinte est échue");
    }

    [Fact]
    public void Un_role_a_duree_limitee_exige_une_echeance_future()
    {
        RoleDefinition role = Role(ScopeKind.Dossier, requiresExpiry: true);

        role.AcceptsExpiry(null, Now).Should().BeFalse();
        role.AcceptsExpiry(Now.AddMinutes(-1), Now).Should().BeFalse();
        role.AcceptsExpiry(Now.AddDays(30), Now).Should().BeTrue();
    }

    [Fact]
    public void Les_modeles_a_duree_limitee_refusent_une_attribution_sans_echeance() =>
        RoleTemplates.All.Values.Where(r => r.RequiresExpiry).Should().NotBeEmpty()
            .And.OnlyContain(r => !r.AcceptsExpiry(null, Now));
}
