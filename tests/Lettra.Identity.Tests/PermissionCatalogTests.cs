using System.Reflection;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Authorization;

namespace Lettra.Identity.Tests;

/// <summary>L'enum <see cref="Permission"/> est partagé par tous les modules, en ajout seulement.</summary>
public sealed class PermissionCatalogTests
{
    [Fact]
    public void Chaque_permission_declare_ce_qu_elle_ouvre() =>
        Enum.GetValues<Permission>()
            .Where(p => typeof(Permission).GetField(p.ToString())!.GetCustomAttribute<PermissionCategoryAttribute>() is null)
            .Should().BeEmpty();

    [Fact]
    public void Les_valeurs_sont_uniques() =>
        Enum.GetValues<Permission>().Select(p => (int)p).Should().OnlyHaveUniqueItems();

    /// <summary>Instantané des valeurs publiées : une renumérotation casserait les rôles stockés et les instantanés hors ligne.</summary>
    [Theory]
    [InlineData(Permission.TiersConsulter, 1)]
    [InlineData(Permission.TiersCreer, 2)]
    [InlineData(Permission.DossierLire, 10)]
    [InlineData(Permission.DossiersCreer, 12)]
    [InlineData(Permission.CatalogueConsulter, 20)]
    [InlineData(Permission.CatalogueGerer, 21)]
    [InlineData(Permission.FactureEmettre, 42)]
    [InlineData(Permission.PlateformeAdministrer, 100)]
    public void Les_valeurs_publiees_ne_changent_pas(Permission permission, int value) =>
        ((int)permission).Should().Be(value);

    [Fact]
    public void Les_roles_fournis_ne_referencent_que_des_permissions_existantes() =>
        RoleTemplates.All.Values.SelectMany(r => r.Permissions).Should().OnlyContain(p => Enum.IsDefined(p));
}
