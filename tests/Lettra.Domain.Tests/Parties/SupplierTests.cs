using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Tests.Parties;

public sealed class SupplierTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private static Supplier.Draft Draft(
        string? siren = null,
        string? siret = "73282932000074",
        string? vat = null,
        string? iban = "FR76 3000 6000 0112 3456 7890 189",
        string country = "FR") =>
        new("Minoterie Dupuis", siren, siret, vat, "factures@dupuis.fr", iban,
            "2 chemin du Moulin", null, country == "FR" ? "45000" : "1000", "Orléans", country);

    [Fact]
    public void Un_fournisseur_francais_est_cree_avec_son_siren_deduit_et_son_iban_compact()
    {
        Result<Supplier> result = Supplier.Create(SupplierId.New(), Draft(), Now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Siren!.Value.Should().Be("732829320");
        result.Value.Iban!.Value.Should().Be("FR7630006000011234567890189");
        result.Value.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<SupplierCreated>();
    }

    [Fact]
    public void Un_fournisseur_francais_sans_siren_est_refuse() =>
        Supplier.Create(SupplierId.New(), Draft(siret: null), Now)
            .Errors.Should().ContainSingle().Which.Should().Be(SupplierErrors.SirenRequired);

    [Fact]
    public void Un_fournisseur_etranger_n_a_pas_besoin_de_siren() =>
        Supplier.Create(SupplierId.New(), Draft(siret: null, iban: "DE89370400440532013000", country: "BE"), Now)
            .IsSuccess.Should().BeTrue();

    [Fact]
    public void Un_iban_a_la_cle_fausse_est_refuse_sous_son_champ() =>
        Supplier.Create(SupplierId.New(), Draft(iban: "FR7630006000011234567890188"), Now)
            .Errors.Should().ContainSingle().Which.Should().Be(SharedKernelErrors.IbanInvalid.For(nameof(Supplier.Iban)));

    [Fact]
    public void Un_numero_de_tva_francais_d_un_autre_siren_est_refuse()
    {
        string otherVat = VatNumber.FromSiren(Siren.Create("552100554").Value).Value;

        Supplier.Create(SupplierId.New(), Draft(vat: otherVat), Now)
            .Errors.Should().ContainSingle().Which.Should().Be(PartyErrors.VatNumberSirenMismatch);
    }

    [Fact]
    public void Le_numero_de_tva_du_meme_siren_est_accepte() =>
        Supplier.Create(SupplierId.New(), Draft(vat: "FR44 732 829 320"), Now).IsSuccess.Should().BeTrue();

    [Fact]
    public void Toutes_les_erreurs_sont_remontees_ensemble() =>
        Supplier.Create(SupplierId.New(), new Supplier.Draft(" ", "123", null, null, "pas-un-mail", "XX", null, null, "75", null, "FR"), Now)
            .Errors.Select(e => e.Code).Should().BeEquivalentTo(
                SupplierErrors.NameRequiredCode,
                SharedKernelErrors.SirenInvalidCode,
                SharedKernelErrors.EmailInvalidCode,
                SharedKernelErrors.IbanInvalidCode,
                SharedKernelErrors.AddressLineRequiredCode,
                SharedKernelErrors.CityRequiredCode,
                SharedKernelErrors.PostalCodeInvalidCode);

    [Fact]
    public void Un_client_avec_un_numero_de_tva_d_un_autre_siren_est_aussi_refuse()
    {
        Customer.Draft draft = new(CustomerKind.Company, "Boulangerie Martin", null, "73282932000074",
            VatNumber.FromSiren(Siren.Create("552100554").Value).Value, null, "1 rue de la Paix", null, "75002", "Paris", "FR");

        Customer.Create(CustomerId.New(), draft, Now).Errors.Should().ContainSingle().Which.Should().Be(PartyErrors.VatNumberSirenMismatch);
    }
}
