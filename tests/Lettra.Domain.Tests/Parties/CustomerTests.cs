using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Tests.Parties;

public sealed class CustomerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private static Customer.Draft Company(string? siren = null, string? siret = "73282932000074", string country = "FR") =>
        new(CustomerKind.Company, "Boulangerie Martin", siren, siret, null, "compta@martin.fr",
            "1 rue de la Paix", null, country == "FR" ? "75002" : "1000", "Paris", country);

    [Fact]
    public void Une_entreprise_francaise_avec_siret_est_creee_et_son_siren_deduit()
    {
        Result<Customer> result = Customer.Create(CustomerId.New(), Company(), Now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Siren!.Value.Should().Be("732829320");
        result.Value.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CustomerCreated>();
    }

    [Fact]
    public void Une_entreprise_francaise_sans_siren_est_refusee() =>
        Customer.Create(CustomerId.New(), Company(siret: null), Now)
            .Errors.Should().ContainSingle().Which.Should().Be(CustomerErrors.SirenRequired);

    [Fact]
    public void Une_entreprise_etrangere_n_a_pas_besoin_de_siren() =>
        Customer.Create(CustomerId.New(), Company(siret: null, country: "BE"), Now).IsSuccess.Should().BeTrue();

    [Fact]
    public void Siret_et_siren_incoherents() =>
        Customer.Create(CustomerId.New(), Company(siren: "552100554"), Now)
            .Errors.Should().ContainSingle().Which.Should().Be(CustomerErrors.SiretSirenMismatch);

    [Fact]
    public void Un_particulier_ne_peut_pas_avoir_de_siren()
    {
        Customer.Draft draft = Company() with { Kind = CustomerKind.Individual };

        Customer.Create(CustomerId.New(), draft, Now)
            .Errors.Should().ContainSingle().Which.Should().Be(CustomerErrors.IndividualCannotHaveCompanyIds);
    }

    [Fact]
    public void Toutes_les_erreurs_de_saisie_sont_renvoyees_ensemble()
    {
        Customer.Draft draft = new(CustomerKind.Company, " ", null, "123", null, "pas-un-email", "", null, "75", "", "FR");

        IReadOnlyList<Error> errors = Customer.Create(CustomerId.New(), draft, Now).Errors;

        errors.Select(e => (e.Code, e.Target)).Should().BeEquivalentTo(
        [
            (CustomerErrors.NameRequiredCode, "Name"),
            (SharedKernelErrors.SiretInvalidCode, "Siret"),
            (SharedKernelErrors.EmailInvalidCode, "Email"),
            (SharedKernelErrors.AddressLineRequiredCode, "Line1"),
            (SharedKernelErrors.CityRequiredCode, "City"),
            (SharedKernelErrors.PostalCodeInvalidCode, "PostalCode"),
        ]);
    }

    [Fact]
    public void Un_nom_trop_long_est_refuse()
    {
        Customer.Draft draft = Company() with { Name = new string('a', Customer.MaxNameLength + 1) };

        Customer.Create(CustomerId.New(), draft, Now).Errors.Should().ContainSingle().Which.Should().Be(CustomerErrors.NameTooLong);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(" ", null)]
    [InlineData("en-us", "en-US")]
    [InlineData("fr-FR", "fr-FR")]
    public void La_langue_d_impression_est_lue_sous_sa_forme_canonique(string? saisie, string? attendue) =>
        Customer.Create(CustomerId.New(), Company() with { PreferredLanguage = saisie }, Now).Value.PreferredLanguage.Should().Be(attendue);

    [Fact]
    public void Une_langue_non_prise_en_charge_est_refusee()
    {
        Customer customer = Customer.Create(CustomerId.New(), Company() with { PreferredLanguage = "en-US" }, Now).Value;

        customer.Update(Company() with { PreferredLanguage = "de-DE" }, Now)
            .Errors.Should().ContainSingle().Which.Target.Should().Be(nameof(Customer.PreferredLanguage));
        customer.PreferredLanguage.Should().Be("en-US", "rien n'est modifié quand la saisie est refusée");
    }
}
