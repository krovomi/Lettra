using System.Security.Claims;
using Lettra.Infrastructure.Identity.Oidc;

namespace Lettra.Identity.Tests;

/// <summary>Lecture des claims Keycloak : qui, quel cabinet, quel niveau d'authentification. Jamais de droits.</summary>
public sealed class OidcClaimsTests
{
    private static readonly OidcOptions Options = new();

    private static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

    [Fact]
    public void Le_sub_doit_etre_un_uuid()
    {
        Guid id = Guid.CreateVersion7();

        OidcClaims.UserId(Principal(new Claim("sub", id.ToString()))).Should().Be(id);
        OidcClaims.UserId(Principal(new Claim("sub", "claire"))).Should().BeNull();
        OidcClaims.UserId(Principal()).Should().BeNull();
    }

    [Fact]
    public void Le_cabinet_vient_de_l_organisation_keycloak()
    {
        Guid cabinet = Guid.CreateVersion7();
        Claim organization = new("organization", $$$"""{"cabinet-dupont":{"id":"{{{cabinet}}}"}}""", "JSON");

        OidcClaims.Cabinet(Principal(organization))!.Value.Value.Should().Be(cabinet);
    }

    [Fact]
    public void Plusieurs_organisations_ou_un_claim_illisible_ne_donnent_aucun_cabinet()
    {
        Claim two = new("organization", $$$"""{"a":{"id":"{{{Guid.CreateVersion7()}}}"},"b":{"id":"{{{Guid.CreateVersion7()}}}"}}""", "JSON");

        OidcClaims.Cabinet(Principal(two)).Should().BeNull();
        OidcClaims.Cabinet(Principal(new Claim("organization", "cabinet-dupont"))).Should().BeNull();
    }

    [Theory]
    [InlineData("otp", true)]
    [InlineData("hwk", true)]
    [InlineData("pwd", false)]
    public void Le_second_facteur_se_lit_dans_amr(string amr, bool strong) =>
        OidcClaims.HasStrongAuthentication(Principal(new Claim("amr", "pwd"), new Claim("amr", amr)), Options).Should().Be(strong);

    [Fact]
    public void Ou_dans_acr() =>
        OidcClaims.HasStrongAuthentication(Principal(new Claim("acr", "2")), Options).Should().BeTrue();
}
