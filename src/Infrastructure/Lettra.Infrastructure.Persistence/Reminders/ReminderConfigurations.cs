using Lettra.Domain.Billing;
using Lettra.Domain.Reminders;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Billing;
using Lettra.Infrastructure.Persistence.Concurrency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Reminders;

/// <summary>Historique des relances (vague 7) : une ligne par envoi, jamais modifiée.</summary>
internal sealed class PaymentReminderConfiguration : IEntityTypeConfiguration<PaymentReminder>
{
    public void Configure(EntityTypeBuilder<PaymentReminder> builder)
    {
        builder.ToTable("PaymentReminders");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.InvoiceId).HasConversion(id => id.Value, value => new InvoiceId(value)).IsRequired();
        builder.Property(r => r.CustomerId).HasConversion(id => id.Value, value => new CustomerId(value)).IsRequired();
        builder.Property(r => r.InvoiceNumber).HasMaxLength(BillingDocumentMapping.NumberLength).IsRequired();
        builder.Property(r => r.Level).HasConversion<int>();
        builder.Property(r => r.Channel).HasConversion<int>();
        builder.Property(r => r.Outcome).HasConversion<int>();
        builder.Property(r => r.Trigger).HasConversion<int>();
        builder.Property(r => r.ErrorCode).HasMaxLength(100);
        builder.Property(r => r.Recipient).HasMaxLength(320);
        builder.Property(r => r.Language).HasMaxLength(10).IsRequired();
        builder.Property(r => r.Subject).HasMaxLength(PaymentReminder.MaxSubjectLength).IsRequired();
        builder.Property(r => r.Body).HasMaxLength(PaymentReminder.MaxBodyLength).IsRequired();
        builder.HasIndex(r => new { r.InvoiceId, r.OccurredOn });
        builder.HasIndex(r => r.CustomerId);
        builder.HasIndex(r => r.OccurredOn);
    }
}

internal sealed class ReminderSettingsConfiguration : IEntityTypeConfiguration<ReminderSettings>
{
    public void Configure(EntityTypeBuilder<ReminderSettings> builder)
    {
        builder.ToTable("ReminderSettings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.IsVersioned();
    }
}
