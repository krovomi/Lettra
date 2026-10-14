using Lettra.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Siren).HasMaxLength(9);
    }
}

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Number).HasMaxLength(32);
        b.HasIndex(x => x.Number).IsUnique();
        b.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
    }
}

internal sealed class NumberSeriesConfiguration : IEntityTypeConfiguration<NumberSeries>
{
    public void Configure(EntityTypeBuilder<NumberSeries> b)
    {
        b.HasKey(x => x.Code);
        b.Property(x => x.Code).HasMaxLength(32);
        b.Property(x => x.Prefix).HasMaxLength(16);
        // Concurrence optimiste neutre : UPDATE ... WHERE Version = @ancienne.
        b.Property(x => x.Version).IsConcurrencyToken();
    }
}

internal sealed class DossierIdentityConfiguration : IEntityTypeConfiguration<DossierIdentity>
{
    public void Configure(EntityTypeBuilder<DossierIdentity> b)
    {
        b.ToTable("DossierIdentity");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
    }
}
