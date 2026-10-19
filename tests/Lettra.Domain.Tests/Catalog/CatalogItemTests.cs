using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Tests.Catalog;

public sealed class CatalogItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private static CatalogItem.Draft Draft(
        string? reference = "PRES-01",
        decimal price = 85m,
        string? unit = "HUR",
        string? vat = "FR-20",
        CatalogItemKind kind = CatalogItemKind.Service) =>
        new(reference, "Heure de conseil", "Accompagnement comptable", kind, unit, price, vat);

    [Fact]
    public void Un_article_est_cree_avec_sa_cle_de_reference_en_majuscules()
    {
        Result<CatalogItem> result = CatalogItem.Create(CatalogItemId.New(), Draft(reference: " pres-01 "), Now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Reference.Should().Be("pres-01");
        result.Value.ReferenceKey.Should().Be("PRES-01");
        result.Value.Unit.Should().Be(UnitOfMeasure.Hour);
        result.Value.VatRate.Should().Be(VatRate.Normal);
        result.Value.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CatalogItemCreated>();
    }

    [Fact]
    public void Le_prix_est_porte_a_quatre_decimales() =>
        CatalogItem.Create(CatalogItemId.New(), Draft(price: 1.234567m), Now).Value.UnitPriceExclTax.Should().Be(1.2346m);

    [Fact]
    public void Toutes_les_erreurs_sont_remontees_ensemble() =>
        CatalogItem.Create(CatalogItemId.New(), new CatalogItem.Draft("REF 01", "", null, (CatalogItemKind)9, "XYZ", -1m, "FR-19"), Now)
            .Errors.Select(e => (e.Code, e.Target)).Should().BeEquivalentTo(
            [
                (CatalogItemErrors.ReferenceInvalidCode, nameof(CatalogItem.Reference)),
                (CatalogItemErrors.DesignationRequiredCode, nameof(CatalogItem.Designation)),
                (CatalogItemErrors.KindInvalidCode, nameof(CatalogItem.Kind)),
                (CatalogItemErrors.UnitUnknownCode, nameof(CatalogItem.Unit)),
                (SharedKernelErrors.VatRateUnknownCode, nameof(CatalogItem.VatRate)),
                (CatalogItemErrors.PriceNegativeCode, nameof(CatalogItem.UnitPriceExclTax)),
            ]);

    [Fact]
    public void Un_prix_trop_grand_est_refuse() =>
        CatalogItem.Create(CatalogItemId.New(), Draft(price: 1_000_000_000m), Now)
            .Errors.Should().ContainSingle().Which.Should().Be(CatalogItemErrors.PriceTooLarge);

    [Fact]
    public void Une_modification_refusee_ne_change_rien()
    {
        CatalogItem item = CatalogItem.Create(CatalogItemId.New(), Draft(), Now).Value;
        item.ClearDomainEvents();

        Result result = item.Update(Draft(price: -5m, reference: "AUTRE"), Now.AddHours(1));

        result.IsFailure.Should().BeTrue();
        item.Reference.Should().Be("PRES-01");
        item.UnitPriceExclTax.Should().Be(85m);
        item.UpdatedAt.Should().Be(Now);
        item.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Une_modification_valide_met_a_jour_l_article()
    {
        CatalogItem item = CatalogItem.Create(CatalogItemId.New(), Draft(), Now).Value;

        item.Update(Draft(price: 90m, vat: "FR-10", unit: "ls"), Now.AddHours(1)).IsSuccess.Should().BeTrue();

        item.UnitPriceExclTax.Should().Be(90m);
        item.VatRate.Should().Be(VatRate.Intermediate);
        item.Unit.Should().Be(UnitOfMeasure.LumpSum);
        item.UpdatedAt.Should().Be(Now.AddHours(1));
        item.DomainEvents.Should().ContainItemsAssignableTo<CatalogItemUpdated>();
    }

    [Fact]
    public void Un_article_archive_ne_se_modifie_plus_mais_se_reactive()
    {
        CatalogItem item = CatalogItem.Create(CatalogItemId.New(), Draft(), Now).Value;
        item.SetArchived(true, Now);

        item.Update(Draft(price: 90m), Now).Errors.Should().ContainSingle().Which.Should().Be(CatalogItemErrors.Archived);

        item.SetArchived(false, Now);
        item.Update(Draft(price: 90m), Now).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Archiver_deux_fois_ne_produit_qu_un_evenement()
    {
        CatalogItem item = CatalogItem.Create(CatalogItemId.New(), Draft(), Now).Value;
        item.ClearDomainEvents();

        item.SetArchived(true, Now);
        item.SetArchived(true, Now);

        item.DomainEvents.Should().ContainSingle().Which.Should().Be(new CatalogItemArchiveChanged(item.Id, true, Now));
    }

    [Fact]
    public void Les_unites_sont_les_codes_cee_onu_de_factur_x() =>
        UnitOfMeasure.All.Select(u => u.Code).Should().Equal("C62", "HUR", "DAY", "MON", "LS", "KGM", "MTR", "MTK", "MTQ", "LTR", "KMT");
}
