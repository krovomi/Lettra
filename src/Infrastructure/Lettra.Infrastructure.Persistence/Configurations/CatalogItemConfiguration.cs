using Lettra.Domain.Catalog;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Concurrency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Configurations;

internal sealed class CatalogItemConfiguration : IEntityTypeConfiguration<CatalogItem>
{
    public void Configure(EntityTypeBuilder<CatalogItem> builder)
    {
        builder.ToTable("CatalogItems");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasConversion(id => id.Value, value => new CatalogItemId(value)).ValueGeneratedNever();
        builder.Property(i => i.Reference).HasMaxLength(CatalogItem.MaxReferenceLength).IsRequired();
        builder.Property(i => i.ReferenceKey).HasMaxLength(CatalogItem.MaxReferenceLength).IsRequired();
        builder.Property(i => i.Designation).HasMaxLength(CatalogItem.MaxDesignationLength).IsRequired();
        builder.Property(i => i.Description).HasMaxLength(CatalogItem.MaxDescriptionLength);
        builder.Property(i => i.Kind).HasConversion<int>();
        builder.Property(i => i.Unit).HasConversion(u => u.Code, code => UnitOfMeasure.FromCode(code).Value).HasMaxLength(3).IsRequired();
        builder.Property(i => i.UnitPriceExclTax).IsRequired();
        builder.Property(i => i.VatRate).HasConversion(r => r.Code, code => VatRate.FromCode(code).Value).HasMaxLength(10).IsRequired();
        builder.Property(i => i.CreatedAt).IsRequired();
        builder.Property(i => i.UpdatedAt).IsRequired();

        builder.IsVersioned();

        // Unicité sans tenir compte de la casse, identique sur SQLite, PostgreSQL et SQL Server.
        builder.HasIndex(i => i.ReferenceKey).IsUnique();
        builder.HasIndex(i => i.Designation);
        builder.Ignore(i => i.DomainEvents);
    }
}
