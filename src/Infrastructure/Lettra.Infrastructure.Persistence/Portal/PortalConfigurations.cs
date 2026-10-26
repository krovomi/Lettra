using Lettra.Domain.Billing;
using Lettra.Domain.Portal;
using Lettra.Domain.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Portal;

/// <summary>Liens d'espace client : seule l'empreinte du secret est gardée, unique.</summary>
internal sealed class PortalLinkConfiguration : IEntityTypeConfiguration<PortalLink>
{
    public void Configure(EntityTypeBuilder<PortalLink> builder)
    {
        builder.ToTable("PortalLinks");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.CustomerId).HasConversion(id => id.Value, value => new CustomerId(value)).IsRequired();
        builder.Property(l => l.SecretHash).HasMaxLength(PortalLink.SecretHashLength).IsRequired();
        builder.Property(l => l.QuoteId).HasConversion(id => id!.Value.Value, value => new QuoteId(value));
        builder.Property(l => l.DownPaymentPercent).HasPrecision(5, 2);
        builder.HasIndex(l => l.SecretHash).IsUnique();
        builder.HasIndex(l => l.CustomerId);
    }
}

/// <summary>Preuves de signature des devis : une par devis, jamais modifiée (sauf l'acompte rattaché).</summary>
internal sealed class QuoteSignatureConfiguration : IEntityTypeConfiguration<QuoteSignature>
{
    public void Configure(EntityTypeBuilder<QuoteSignature> builder)
    {
        builder.ToTable("QuoteSignatures");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.QuoteId).HasConversion(id => id.Value, value => new QuoteId(value)).IsRequired();
        builder.Property(s => s.SignerName).HasMaxLength(QuoteSignature.MaxSignerNameLength).IsRequired();
        builder.Property(s => s.DocumentHash).HasMaxLength(128).IsRequired();
        builder.Property(s => s.Provider).HasMaxLength(QuoteSignature.MaxProviderLength).IsRequired();
        builder.Property(s => s.ProviderReference).HasMaxLength(QuoteSignature.MaxReferenceLength).IsRequired();
        builder.Property(s => s.DownPaymentInvoiceId).HasConversion(id => id!.Value.Value, value => new InvoiceId(value));
        builder.HasIndex(s => s.QuoteId).IsUnique();
    }
}
