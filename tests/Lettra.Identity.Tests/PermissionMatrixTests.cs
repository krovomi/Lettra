using System.Collections.Frozen;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Authorization;
using Lettra.Domain.SharedKernel;
using static Lettra.Application.Abstractions.Identity.Permission;

namespace Lettra.Identity.Tests;

/// <summary>
/// Matrice rôle × permission × portée (annexe F §3.2 et §4, ADR-0010), jouée à chaque build. Le tableau attendu est
/// écrit ici, indépendamment de <see cref="RoleTemplates"/> : modifier un rôle oblige à modifier ce tableau, donc à
/// le faire relire.
/// </summary>
public sealed class PermissionMatrixTests
{
    /// <summary>Ce que chaque rôle peut faire dans un dossier de sa portée. Tout le reste est refusé.</summary>
    private static readonly FrozenDictionary<string, Permission[]> Expected = new Dictionary<string, Permission[]>
    {
        [RoleTemplates.AdministrateurPlateforme] = [PlateformeAdministrer],
        [RoleTemplates.AdministrateurCabinet] = [CabinetAdministrer, UtilisateursGerer, GroupeGerer, DossierAdministrer, DossiersCreer, AlertesLire, AlertesConfigurer],
        [RoleTemplates.Manager] =
        [
            TiersConsulter, TiersCreer, TiersModifier, DossierLire, CatalogueConsulter, CatalogueGerer, DevisCreer, FactureSaisir, FactureValider,
            FactureEmettre, AvoirCreer, AvoirApprouver, FecExporter, IaUtiliser, DelegationsApprouver, AlertesLire, AlertesTraiter,
        ],
        [RoleTemplates.AdministrateurGroupe] = [GroupeGerer, DossierAdministrer],
        [RoleTemplates.CollaborateurEtendu] =
        [
            TiersConsulter, TiersCreer, TiersModifier, DossierLire, CatalogueConsulter, DevisCreer, FactureSaisir, FactureValider, FactureEmettre,
            IaUtiliser, AlertesLire,
        ],
        [RoleTemplates.Collaborateur] = [TiersConsulter, TiersCreer, TiersModifier, DossierLire, CatalogueConsulter, DevisCreer, FactureSaisir, IaUtiliser, AlertesLire],
        [RoleTemplates.Auditeur] = [TiersConsulter, CatalogueConsulter, DossierLire, FecExporter],
        [RoleTemplates.ClientPortail] = [PortailConsulter, DevisAccepter, PiecesDeposer],
        [RoleTemplates.ExploitantSurSite] = [AlertesLire, AlertesTraiter],
        [RoleTemplates.DelegationAvoirsExports] = [AvoirCreer, FecExporter],
    }.ToFrozenDictionary();

    /// <summary>Relation entre l'attribution et le dossier visé.</summary>
    public enum Relation
    {
        DansLaPortee,
        AutreDossierDuCabinet,
        AutreCabinet,
        AttributionEchue,
        CompteSuspendu,
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly OrganisationId Cabinet = new(Guid.Parse("00000000-0000-7000-8000-0000000000c1"));
    private static readonly OrganisationId OtherCabinet = new(Guid.Parse("00000000-0000-7000-8000-0000000000c2"));
    private static readonly GroupId Group = new(Guid.Parse("00000000-0000-7000-8000-0000000000a1"));
    private static readonly GroupId OtherGroup = new(Guid.Parse("00000000-0000-7000-8000-0000000000a2"));
    private static readonly DossierId Dossier = new(Guid.Parse("00000000-0000-7000-8000-0000000000d1"));
    private static readonly DossierId OtherDossier = new(Guid.Parse("00000000-0000-7000-8000-0000000000d2"));
    private static readonly Guid User = Guid.Parse("00000000-0000-7000-8000-0000000000e1");

    public static TheoryData<string, Permission, Relation> Cases()
    {
        TheoryData<string, Permission, Relation> data = new();
        foreach (string role in RoleTemplates.All.Keys.Order(StringComparer.Ordinal))
        {
            foreach (Permission permission in Enum.GetValues<Permission>())
            {
                foreach (Relation relation in Enum.GetValues<Relation>())
                {
                    data.Add(role, permission, relation);
                }
            }
        }

        return data;
    }

    [Fact]
    public void Le_tableau_couvre_tous_les_roles_fournis() =>
        Expected.Keys.Should().BeEquivalentTo(RoleTemplates.All.Keys);

    [Theory]
    [MemberData(nameof(Cases))]
    public void Matrice(string role, Permission permission, Relation relation)
    {
        RoleDefinition definition = RoleTemplates.All[role];
        (UserAuthorizationProfile profile, ScopePath target) = Scenario(definition, relation);

        bool granted = PermissionResolver.Resolve(profile, target, Now).Contains(permission);

        bool expected = relation switch
        {
            Relation.DansLaPortee => Expected[role].Contains(permission),
            // Un rôle de cabinet ou de plateforme couvre tous les dossiers du cabinet.
            Relation.AutreDossierDuCabinet => definition.WidestScope <= ScopeKind.Cabinet && Expected[role].Contains(permission),
            // La plateforme n'appartient à aucun cabinet ; ses permissions n'ouvrent aucune donnée.
            Relation.AutreCabinet => definition.WidestScope == ScopeKind.Plateforme && Expected[role].Contains(permission),
            _ => false,
        };
        granted.Should().Be(expected, $"{role} / {permission} / {relation}");
    }

    [Fact]
    public void Aucune_permission_de_plateforme_ou_d_administration_n_ouvre_un_dossier()
    {
        foreach (string role in new[] { RoleTemplates.AdministrateurPlateforme, RoleTemplates.AdministrateurCabinet, RoleTemplates.AdministrateurGroupe, RoleTemplates.ExploitantSurSite })
        {
            PermissionCatalog.OpensDossierData(RoleTemplates.All[role].Permissions).Should().BeFalse(role);
        }
    }

    /// <summary>Attribution à la portée naturelle du rôle (la plus étroite qui le rende utile), et le dossier visé.</summary>
    private static (UserAuthorizationProfile, ScopePath) Scenario(RoleDefinition role, Relation relation)
    {
        AuthorizationScope scope = role.WidestScope switch
        {
            ScopeKind.Plateforme => AuthorizationScope.Plateforme,
            ScopeKind.Cabinet => AuthorizationScope.ForCabinet(Cabinet),
            ScopeKind.Groupe => AuthorizationScope.ForGroup(Group),
            _ => AuthorizationScope.ForDossier(Dossier),
        };
        DateTimeOffset expires = relation == Relation.AttributionEchue ? Now.AddMinutes(-1) : Now.AddDays(30);
        UserAuthorizationProfile profile = new(
            User,
            relation == Relation.CompteSuspendu ? AccountState.Suspended : AccountState.Active,
            Cabinet,
            [Group],
            [new RoleAssignment(new SubjectRef(SubjectKind.User, User), role.Key, scope, expires)],
            []);

        ScopePath target = relation switch
        {
            Relation.AutreDossierDuCabinet => DossierPath(OtherDossier, Cabinet, OtherGroup),
            Relation.AutreCabinet => DossierPath(OtherDossier, OtherCabinet, OtherGroup),
            _ => DossierPath(Dossier, Cabinet, Group),
        };
        return (profile, target);
    }

    internal static ScopePath DossierPath(DossierId dossier, OrganisationId cabinet, params GroupId[] groups) =>
        new(AuthorizationScope.ForDossier(dossier), cabinet,
        [
            AuthorizationScope.ForDossier(dossier), .. groups.Select(AuthorizationScope.ForGroup),
            AuthorizationScope.ForCabinet(cabinet), AuthorizationScope.Plateforme,
        ]);
}
