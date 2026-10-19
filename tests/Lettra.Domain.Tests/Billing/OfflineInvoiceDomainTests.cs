using Lettra.Domain.Billing;
using Lettra.Domain.Catalog;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Tests.Billing;

/// <summary>Pièces numérotées sur un poste hors ligne (ADR-0007) et reconstruites au serveur.</summary>
public sealed class OfflineInvoiceDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 15);
    private static readonly Guid User = Guid.CreateVersion7();
    private static readonly Guid Device = Guid.CreateVersion7();

    private static readonly PartySnapshot Martin = new(
        "Boulangerie Martin", "1 rue de la Paix", null, "75002", "Paris", "FR", "732829320", "73282932000074", "FR44732829320",
        null, null, null, IsBusiness: true);

    private static readonly SellerTerms Terms = new(
        new PartySnapshot("Conseil Lettra", "10 avenue de l'Opéra", null, "75001", "Paris", "FR", "552100554", "55210055400013", "FR96552100554",
            "SAS", 10_000m, "RCS Paris 552 100 554", IsBusiness: true),
        VatFranchise: false, VatOnDebits: false, PaymentDays: 30, PaymentTerms: "Virement à 30 jours", LatePenaltyRate: 10m,
        EarlyPaymentDiscount: null);

    private static readonly LineDraft Conseil = new(null, null, "Conseil", null, CatalogItemKind.Service, "HUR", 2, 100m, 0m, "FR-20", null);

    private static Invoice Draft() =>
        Invoice.Create(InvoiceId.New(), new CustomerId(Guid.CreateVersion7()), Martin, new InvoiceHeader(null, null, null, null, null, VatRegime.Standard, null, null), [Conseil], User, Now).Value;

    private static Invoice ValidatedOnDevice()
    {
        Invoice invoice = Draft();
        invoice.NumberOnDevice("FAC-P01", Device, User, Now).IsSuccess.Should().BeTrue();
        invoice.Validate("FAC-P01-000001", Today, Martin, Terms, User, Now).IsSuccess.Should().BeTrue();
        return invoice;
    }

    private static InvoiceReplica ReplicaOf(Invoice i) => new(
        i.Id, i.Kind, i.Status, i.SeriesCode, i.DeviceId, i.Number!, i.CustomerId, i.Customer, i.Seller!,
        new InvoiceHeader(i.DeliveryDate, i.DueDate, i.PaymentTerms, i.DeliveryAddress, i.PurchaseOrderReference, i.VatRegime, i.Subject, i.Notes),
        i.IssueDate!.Value, i.LatePenaltyRate, i.EarlyPaymentDiscount, i.LegalMentionList, i.QuoteId, i.QuoteNumber, i.CreditedInvoiceId,
        i.CreditedInvoiceNumber, i.CreditReason, [.. i.Lines.Select(l => l.ToDraft())], new HashSet<int>(), i.TotalExclTax, i.TotalVat, i.TotalInclTax,
        i.CreatedBy, i.CreatedAt, i.ValidatedBy!.Value, i.ValidatedAt!.Value, i.IssuedBy, i.IssuedAt, i.CreditApprovedBy, i.CreditApprovedAt);

    [Fact]
    public void Un_brouillon_numerote_sur_le_poste_prend_la_serie_du_poste_et_avance_sa_version()
    {
        Invoice invoice = Draft();
        invoice.NumberOnDevice("FAC-P01", Device, User, Now).IsSuccess.Should().BeTrue();
        invoice.SeriesCode.Should().Be("FAC-P01");
        invoice.DeviceId.Should().Be(Device);
        invoice.Version.Should().Be(2);

        invoice.Validate("FAC-P01-000001", Today, Martin, Terms, User, Now).IsSuccess.Should().BeTrue();
        invoice.NumberOnDevice("FAC-P02", Device, User, Now).Errors.Should().ContainSingle(e => e.Code == BillingErrors.NotDraftCode);
        FluentActions.Invoking(() => Draft().NumberOnDevice("DEV-P01", Device, User, Now)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void La_replique_redonne_la_meme_piece_validee_en_version_1()
    {
        Invoice source = ValidatedOnDevice();
        Invoice replica = Invoice.Replicate(ReplicaOf(source)).Value;

        replica.Status.Should().Be(InvoiceStatus.Validated);
        replica.Version.Should().Be(1);
        replica.Number.Should().Be("FAC-P01-000001");
        replica.SeriesCode.Should().Be("FAC-P01");
        replica.LegalMentionList.Should().Equal(source.LegalMentionList);
        replica.VatSummaries.Should().Equal(source.VatSummaries);
        replica.CreatedBy.Should().Be(source.CreatedBy);
        replica.ContentHash.Should().BeNull("l'empreinte se recalcule et se pose par Seal");
    }

    [Fact]
    public void La_replique_refuse_un_brouillon_et_des_totaux_divergents()
    {
        InvoiceReplica replica = ReplicaOf(ValidatedOnDevice());

        Invoice.Replicate(replica with { Status = InvoiceStatus.Draft }).Errors.Should().ContainSingle(e => e.Code == BillingErrors.ReplicaMismatchCode);
        Invoice.Replicate(replica with { TotalInclTax = replica.TotalInclTax + 0.01m }).Errors.Should().ContainSingle(e => e.Code == BillingErrors.ReplicaMismatchCode);
        Invoice.Replicate(replica with { DownPaymentPositions = new HashSet<int> { 7 } }).Errors.Should().ContainSingle(e => e.Code == BillingErrors.ReplicaMismatchCode);
    }
}
