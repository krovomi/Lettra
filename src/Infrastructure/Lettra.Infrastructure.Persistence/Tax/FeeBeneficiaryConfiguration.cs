using Lettra.Domain.Parties;
using Lettra.Infrastructure.Persistence.Concurrency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Tax;

/// <summary>
/// Un fournisseur suivi pour la déclaration des honoraires (DAS 2) : une ligne par bénéficiaire, de même identifiant que
/// le fournisseur ; les sommes versées hors relevés, une par exercice, à côté.
/// </summary>
internal sealed class FeeBeneficiaryConfiguration : IEntityTypeConfiguration<FeeBeneficiary>
{
    public void Configure(EntityTypeBuilder<FeeBeneficiary> builder)
    {
        builder.ToTable("FeeBeneficiaries");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasConversion(id => id.Value, value => new SupplierId(value)).ValueGeneratedNever();
        builder.Property(b => b.Nature).HasConversion<int>();
        builder.Property(b => b.Profession).HasMaxLength(FeeBeneficiary.MaxProfessionLength).IsRequired();
        builder.HasOne<Supplier>().WithOne().HasForeignKey<FeeBeneficiary>(b => b.Id).OnDelete(DeleteBehavior.Cascade);
        builder.IsVersioned();

        builder.OwnsMany(b => b.PaidOutside, paid =>
        {
            paid.ToTable("FeePaidOutside");
            paid.WithOwner().HasForeignKey("FeeBeneficiaryId");
            paid.HasKey("FeeBeneficiaryId", nameof(FeePaidOutside.FiscalYearStart));
            paid.Property(p => p.Amount).HasPrecision(18, 2);
        });
        builder.Navigation(b => b.PaidOutside).HasField("_paidOutside").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
