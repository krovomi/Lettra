using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Tests.SharedKernel;

public sealed class IdentifiersTests
{
    [Theory]
    [InlineData("732829320")]
    [InlineData("732 829 320")]
    [InlineData("552100554")]
    public void Siren_valide(string raw) => Siren.Create(raw).IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("732829321")]
    [InlineData("73282932")]
    [InlineData("73282932A")]
    public void Siren_invalide(string? raw) =>
        Siren.Create(raw).Errors.Should().ContainSingle().Which.Code.Should().Be(SharedKernelErrors.SirenInvalidCode);

    [Theory]
    [InlineData("73282932000074")]
    [InlineData("552 100 554 00013")]
    [InlineData("35600000000001")] // La Poste : somme des chiffres multiple de 5, Luhn faux.
    public void Siret_valide(string raw) => Siret.Create(raw).IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData("73282932000075")]
    [InlineData("7328293200007")]
    [InlineData("35600000000002")]
    public void Siret_invalide(string raw) => Siret.Create(raw).IsFailure.Should().BeTrue();

    [Fact]
    public void Le_siret_porte_le_siren() =>
        Siret.Create("73282932000074").Value.Siren.Value.Should().Be("732829320");

    [Theory]
    [InlineData("FR44732829320")]
    [InlineData("fr 44 732829320")]
    [InlineData("BE0123456789")]
    public void Numero_de_tva_valide(string raw) => VatNumber.Create(raw).IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData("FR45732829320")]
    [InlineData("FR44732829321")]
    [InlineData("12345")]
    [InlineData("FR")]
    public void Numero_de_tva_invalide(string raw) => VatNumber.Create(raw).IsFailure.Should().BeTrue();

    [Fact]
    public void Numero_de_tva_francais_deduit_du_siren() =>
        VatNumber.FromSiren(Siren.Create("732829320").Value).Value.Should().Be("FR44732829320");

    [Theory]
    [InlineData("contact@exemple.fr", true)]
    [InlineData("contact@exemple", false)]
    [InlineData("pas un e-mail", false)]
    [InlineData("Jean <jean@exemple.fr>", false)]
    public void Email(string raw, bool valid) => Domain.SharedKernel.Email.Create(raw).IsSuccess.Should().Be(valid);

    [Fact]
    public void Adresse_francaise_exige_un_code_postal_a_5_chiffres()
    {
        Result<PostalAddress> address = PostalAddress.Create("1 rue de la Paix", null, "7500", "Paris", "FR");

        address.Errors.Should().ContainSingle()
            .Which.Should().Be(SharedKernelErrors.PostalCodeInvalid.For(nameof(PostalAddress.PostalCode)));
    }

    [Fact]
    public void Adresse_etrangere_accepte_un_code_postal_libre() =>
        PostalAddress.Create("Rue Neuve 1", null, "B-1000", "Bruxelles", "be").Value.Country.Value.Should().Be("BE");

    [Fact]
    public void Adresse_accumule_toutes_les_erreurs() =>
        PostalAddress.Create("", null, "x", "", "FR").Errors.Select(e => e.Code).Should().BeEquivalentTo(
            SharedKernelErrors.AddressLineRequiredCode, SharedKernelErrors.CityRequiredCode, SharedKernelErrors.PostalCodeInvalidCode);
}
