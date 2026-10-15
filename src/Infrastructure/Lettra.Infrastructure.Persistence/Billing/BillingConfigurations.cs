using Lettra.Domain.Billing;
using Lettra.Domain.Pricing;
using Lettra.Domain.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Billing;

/// <summary>
/// Colonnes communes des devis et factures : copies des tiers à plat, lignes et récapitulatif de TVA dans leurs tables
/// (clé = pièce + rang), version domaine comme jeton de concurrence (<c>UPDATE ... WHERE Version = @lue</c>).
/// </summary>
internal static class BillingDocumentMapping
{
    public const int NumberLength = 32;
    public const int SeriesLength = 16;

    public static void MapCommon<TDocument, TId>(EntityTypeBuilder<TDocument> builder, string table, Func<TId, Guid> toGuid, Func<Guid, TId> fromGuid)
        where TDocument : BillingDocument<TId>
        where TId : struct, IEquatable<TId>
    {
        builder.ToTable(table);
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasConversion(id => toGuid(id), value => fromGuid(value)).ValueGeneratedNever();
        builder.Property(d => d.Number).HasMaxLength(NumberLength);
        builder.Property(d => d.SeriesCode).HasMaxLength(SeriesLength).IsRequired();
        builder.Property(d => d.CustomerId).HasConversion(id => id.Value, value => new CustomerId(value)).IsRequired();
        builder.Property(d => d.VatRegime).HasConversion<int>();
        builder.Property(d => d.Subject).HasMaxLength(BillingDocument<TId>.MaxSubjectLength);
        builder.Property(d => d.Notes).HasMaxLength(BillingDocument<TId>.MaxNotesLength);
        builder.Property(d => d.LegalMentions).HasMaxLength(4000).IsRequired();
        builder.Property(d => d.Version).IsConcurrencyToken();
        builder.Ignore(d => d.LegalMentionList);
        builder.Ignore(d => d.OperationCategory);
        builder.Ignore(d => d.IsDraft);
        builder.Ignore(d => d.DomainEvents);

        // Type complexe (valeur) et non entité possédée : une copie peut passer d'une pièce à l'autre (devis → facture).
        builder.ComplexProperty(d => d.Customer, party => MapParty(party, "Customer"));

        builder.OwnsMany(d => d.Lines, line =>
        {
            line.ToTable(table.TrimEnd('s') + "Lines");
            line.WithOwner().HasForeignKey("DocumentId");
            line.HasKey("DocumentId", nameof(DocumentLine.Position));
            line.Property(l => l.Position).ValueGeneratedNever();
            line.Property(l => l.Reference).HasMaxLength(50);
            line.Property(l => l.Designation).HasMaxLength(DocumentLine.MaxDesignationLength).IsRequired();
            line.Property(l => l.Description).HasMaxLength(DocumentLine.MaxDescriptionLength);
            line.Property(l => l.Nature).HasConversion<int>();
            line.Property(l => l.UnitCode).HasMaxLength(3).IsRequired();
            line.Property(l => l.VatRateCode).HasMaxLength(10).IsRequired();
            line.Property(l => l.VatCategory).HasMaxLength(2).IsRequired();
            line.Property(l => l.VatExemptionCode).HasMaxLength(30);
            line.Ignore(l => l.Kind);
        });
        builder.Navigation(d => d.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(d => d.VatSummaries, vat =>
        {
            vat.ToTable(table.TrimEnd('s') + "VatSummaries");
            vat.WithOwner().HasForeignKey("DocumentId");
            // Une ligne par couple (catégorie, taux) : c'est la clé du regroupement de Lettra.Calculations.
            vat.HasKey("DocumentId", nameof(VatSummary.Category), nameof(VatSummary.RatePercent));
            vat.Property(v => v.Category).HasMaxLength(2).IsRequired();
            vat.Property(v => v.ExemptionCode).HasMaxLength(30);
        });
        builder.Navigation(d => d.VatSummaries).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(d => new { d.SeriesCode, d.Number }).IsUnique();
        builder.HasIndex(d => d.CustomerId);
        builder.HasIndex(d => d.IssueDate);
    }

    public static void MapParty(ComplexPropertyBuilder<PartySnapshot> party, string prefix)
    {
        party.Property(p => p.Name).HasColumnName(prefix + "Name").HasMaxLength(200).IsRequired();
        party.Property(p => p.AddressLine1).HasColumnName(prefix + "AddressLine1").HasMaxLength(PostalAddress.MaxLineLength).IsRequired();
        party.Property(p => p.AddressLine2).HasColumnName(prefix + "AddressLine2").HasMaxLength(PostalAddress.MaxLineLength);
        party.Property(p => p.PostalCode).HasColumnName(prefix + "PostalCode").HasMaxLength(16).IsRequired();
        party.Property(p => p.City).HasColumnName(prefix + "City").HasMaxLength(PostalAddress.MaxLineLength).IsRequired();
        party.Property(p => p.CountryCode).HasColumnName(prefix + "Country").HasMaxLength(2).IsRequired();
        party.Property(p => p.Siren).HasColumnName(prefix + "Siren").HasMaxLength(9);
        party.Property(p => p.Siret).HasColumnName(prefix + "Siret").HasMaxLength(14);
        party.Property(p => p.VatNumber).HasColumnName(prefix + "VatNumber").HasMaxLength(15);
        party.Property(p => p.LegalForm).HasColumnName(prefix + "LegalForm").HasMaxLength(100);
        party.Property(p => p.ShareCapital).HasColumnName(prefix + "ShareCapital");
        party.Property(p => p.Rcs).HasColumnName(prefix + "Rcs").HasMaxLength(100);
        party.Property(p => p.IsBusiness).HasColumnName(prefix + "IsBusiness");
        party.Ignore(p => p.SingleLineAddress);
    }
}

internal sealed class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> builder)
    {
        BillingDocumentMapping.MapCommon<Quote, QuoteId>(builder, "Quotes", id => id.Value, value => new QuoteId(value));
        builder.Property(q => q.Status).HasConversion<int>();
        builder.Property(q => q.FinalInvoiceId).HasConversion(id => id!.Value.Value, value => new InvoiceId(value));
        builder.HasIndex(q => q.Status);
    }
}

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        BillingDocumentMapping.MapCommon<Invoice, InvoiceId>(builder, "Invoices", id => id.Value, value => new InvoiceId(value));
        builder.Property(i => i.Kind).HasConversion<int>();
        builder.Property(i => i.Status).HasConversion<int>();
        builder.Property(i => i.PaymentTerms).HasMaxLength(Invoice.MaxPaymentTermsLength);
        builder.Property(i => i.DeliveryAddress).HasMaxLength(Invoice.MaxDeliveryAddressLength);
        builder.Property(i => i.PurchaseOrderReference).HasMaxLength(Invoice.MaxPurchaseOrderLength);
        builder.Property(i => i.EarlyPaymentDiscount).HasMaxLength(500);
        builder.Property(i => i.QuoteId).HasConversion(id => id!.Value.Value, value => new QuoteId(value));
        builder.Property(i => i.QuoteNumber).HasMaxLength(BillingDocumentMapping.NumberLength);
        builder.Property(i => i.CreditedInvoiceId).HasConversion(id => id!.Value.Value, value => new InvoiceId(value));
        builder.Property(i => i.CreditedInvoiceNumber).HasMaxLength(BillingDocumentMapping.NumberLength);
        builder.Property(i => i.CreditReason).HasMaxLength(Invoice.MaxCreditReasonLength);
        builder.Property(i => i.ContentHash).HasMaxLength(64);
        builder.Ignore(i => i.IsDerived);
        builder.Ignore(i => i.CreditableBalance);
        builder.Ignore(i => i.RevenueLines);
        builder.ComplexProperty(i => i.Seller, party => BillingDocumentMapping.MapParty(party, "Seller"));
        builder.HasIndex(i => i.QuoteId);
        builder.HasIndex(i => i.CreditedInvoiceId);
        builder.HasIndex(i => new { i.Kind, i.Status });
    }
}

internal sealed class CompanyProfileConfiguration : IEntityTypeConfiguration<CompanyProfile>
{
    public void Configure(EntityTypeBuilder<CompanyProfile> builder)
    {
        builder.ToTable("CompanyProfile");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Name).HasMaxLength(CompanyProfile.MaxNameLength).IsRequired();
        builder.Property(p => p.LegalForm).HasMaxLength(100);
        builder.Property(p => p.Siren).HasConversion(v => v!.Value, v => Siren.Create(v).Value).HasMaxLength(9);
        builder.Property(p => p.Siret).HasConversion(v => v!.Value, v => Siret.Create(v).Value).HasMaxLength(14);
        builder.Property(p => p.Rcs).HasMaxLength(100);
        builder.Property(p => p.VatNumber).HasConversion(v => v!.Value, v => VatNumber.Create(v).Value).HasMaxLength(15);
        builder.Property(p => p.PaymentTerms).HasMaxLength(500);
        builder.Property(p => p.EarlyPaymentDiscount).HasMaxLength(500);
        builder.Property(p => p.Iban).HasConversion(v => v!.Value, v => Iban.Create(v).Value).HasMaxLength(34);
        builder.Property(p => p.Bic).HasMaxLength(11);
        builder.Property(p => p.Version).IsConcurrencyToken();
        builder.ComplexProperty(p => p.Address, address =>
        {
            address.Property(a => a.Line1).HasColumnName("AddressLine1").HasMaxLength(PostalAddress.MaxLineLength).IsRequired();
            address.Property(a => a.Line2).HasColumnName("AddressLine2").HasMaxLength(PostalAddress.MaxLineLength);
            address.Property(a => a.PostalCode).HasColumnName("PostalCode").HasMaxLength(16).IsRequired();
            address.Property(a => a.City).HasColumnName("City").HasMaxLength(PostalAddress.MaxLineLength).IsRequired();
            address.Property(a => a.Country)
                .HasColumnName("Country")
                .HasConversion(v => v.Value, v => CountryCode.Create(v).Value)
                .HasMaxLength(2)
                .IsRequired();
        });
        builder.Ignore(p => p.DomainEvents);
    }
}

internal sealed class PriceListConfiguration : IEntityTypeConfiguration<PriceList>
{
    public void Configure(EntityTypeBuilder<PriceList> builder)
    {
        builder.ToTable("PriceLists");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasConversion(id => id.Value, value => new PriceListId(value)).ValueGeneratedNever();
        builder.Property(l => l.Name).HasMaxLength(PriceList.MaxNameLength).IsRequired();
        builder.Property(l => l.Version).IsConcurrencyToken();
        builder.Ignore(l => l.DomainEvents);

        builder.OwnsMany(l => l.Entries, entry =>
        {
            entry.ToTable("PriceListEntries");
            entry.WithOwner().HasForeignKey("PriceListId");
            entry.Property(e => e.CatalogItemId).HasConversion(id => id.Value, value => new Domain.Catalog.CatalogItemId(value));
            entry.HasKey("PriceListId", nameof(PriceListEntry.CatalogItemId));
        });
        builder.Navigation(l => l.Entries).HasField("_entries").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(l => l.Customers, customer =>
        {
            customer.ToTable("PriceListCustomers");
            customer.WithOwner().HasForeignKey("PriceListId");
            customer.Property(c => c.CustomerId).HasConversion(id => id.Value, value => new CustomerId(value));
            customer.HasKey("PriceListId", nameof(PriceListCustomer.CustomerId));
            // Un client n'a qu'une grille.
            customer.HasIndex(c => c.CustomerId).IsUnique();
        });
        builder.Navigation(l => l.Customers).HasField("_customers").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
