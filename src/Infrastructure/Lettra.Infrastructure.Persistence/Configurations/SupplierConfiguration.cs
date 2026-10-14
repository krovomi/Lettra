using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Configurations;

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasConversion(id => id.Value, value => new SupplierId(value)).ValueGeneratedNever();
        builder.Property(s => s.Name).HasMaxLength(Supplier.MaxNameLength).IsRequired();
        builder.Property(s => s.Siren).HasConversion(v => v!.Value, v => Siren.Create(v).Value).HasMaxLength(9);
        builder.Property(s => s.Siret).HasConversion(v => v!.Value, v => Siret.Create(v).Value).HasMaxLength(14);
        builder.Property(s => s.VatNumber).HasConversion(v => v!.Value, v => VatNumber.Create(v).Value).HasMaxLength(15);
        builder.Property(s => s.Email).HasConversion(v => v!.Value, v => Email.Create(v).Value).HasMaxLength(254);
        builder.Property(s => s.Iban).HasConversion(v => v!.Value, v => Iban.Create(v).Value).HasMaxLength(34);
        builder.Property(s => s.CreatedAt).IsRequired();

        builder.ComplexProperty(s => s.Address, address =>
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

        builder.HasIndex(s => s.Name);
        builder.HasIndex(s => s.Siren);
        builder.Ignore(s => s.DomainEvents);
    }
}
