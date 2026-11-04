using Lettra.Domain.Billing;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Concurrency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Billing;

/// <summary>Historique des relances de devis : une ligne par relance, jamais modifiée.</summary>
internal sealed class QuoteFollowUpConfiguration : IEntityTypeConfiguration<QuoteFollowUp>
{
    public void Configure(EntityTypeBuilder<QuoteFollowUp> builder)
    {
        builder.ToTable("QuoteFollowUps");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.QuoteId).HasConversion(id => id.Value, value => new QuoteId(value)).IsRequired();
        builder.Property(f => f.CustomerId).HasConversion(id => id.Value, value => new CustomerId(value)).IsRequired();
        builder.Property(f => f.QuoteNumber).HasMaxLength(BillingDocumentMapping.NumberLength).IsRequired();
        builder.Property(f => f.Channel).HasConversion<int>();
        builder.Property(f => f.Outcome).HasConversion<int>();
        builder.Property(f => f.ErrorCode).HasMaxLength(100);
        builder.Property(f => f.Recipient).HasMaxLength(320);
        builder.Property(f => f.Language).HasMaxLength(10).IsRequired();
        builder.Property(f => f.Subject).HasMaxLength(QuoteFollowUp.MaxSubjectLength).IsRequired();
        builder.Property(f => f.Body).HasMaxLength(QuoteFollowUp.MaxBodyLength).IsRequired();
        builder.HasIndex(f => new { f.QuoteId, f.OccurredOn });
        builder.HasIndex(f => f.CustomerId);
        builder.HasIndex(f => f.OccurredOn);
    }
}

internal sealed class QuoteFollowUpSettingsConfiguration : IEntityTypeConfiguration<QuoteFollowUpSettings>
{
    public void Configure(EntityTypeBuilder<QuoteFollowUpSettings> builder)
    {
        builder.ToTable("QuoteFollowUpSettings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.IsVersioned();
    }
}
