using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasConversion(id => id.Value, value => new CustomerId(value)).ValueGeneratedNever();
        builder.Property(c => c.Kind).HasConversion<int>();
        builder.Property(c => c.Name).HasMaxLength(Customer.MaxNameLength).IsRequired();
        builder.Property(c => c.Siren).HasConversion(v => v!.Value, v => Siren.Create(v).Value).HasMaxLength(9);
        builder.Property(c => c.Siret).HasConversion(v => v!.Value, v => Siret.Create(v).Value).HasMaxLength(14);
        builder.Property(c => c.VatNumber).HasConversion(v => v!.Value, v => VatNumber.Create(v).Value).HasMaxLength(15);
        builder.Property(c => c.Email).HasConversion(v => v!.Value, v => Email.Create(v).Value).HasMaxLength(254);
        builder.Property(c => c.CreatedAt).IsRequired();

        builder.ComplexProperty(c => c.BillingAddress, address =>
        {
            address.Property(a => a.Line1).HasColumnName("BillingLine1").HasMaxLength(PostalAddress.MaxLineLength).IsRequired();
            address.Property(a => a.Line2).HasColumnName("BillingLine2").HasMaxLength(PostalAddress.MaxLineLength);
            address.Property(a => a.PostalCode).HasColumnName("BillingPostalCode").HasMaxLength(16).IsRequired();
            address.Property(a => a.City).HasColumnName("BillingCity").HasMaxLength(PostalAddress.MaxLineLength).IsRequired();
            address.Property(a => a.Country)
                .HasColumnName("BillingCountry")
                .HasConversion(v => v.Value, v => CountryCode.Create(v).Value)
                .HasMaxLength(2)
                .IsRequired();
        });

        builder.HasIndex(c => c.Name);
        builder.HasIndex(c => c.Siren);
        builder.Ignore(c => c.DomainEvents);
    }
}
