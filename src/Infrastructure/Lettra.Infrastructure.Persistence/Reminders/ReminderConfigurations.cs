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
        builder.Property(r => r.Installment);
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
        builder.Ignore(s => s.Prevention);
        builder.IsVersioned();
    }
}

/// <summary>
/// Échéanciers demandés dans l'espace client : les échéances sont figées à la demande (table à part), le suivi se lit
/// sur le reste dû de la facture.
/// </summary>
internal sealed class PaymentPlanConfiguration : IEntityTypeConfiguration<PaymentPlan>
{
    public void Configure(EntityTypeBuilder<PaymentPlan> builder)
    {
        builder.ToTable("PaymentPlans");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.InvoiceId).HasConversion(id => id.Value, value => new InvoiceId(value)).IsRequired();
        builder.Property(p => p.CustomerId).HasConversion(id => id.Value, value => new CustomerId(value)).IsRequired();
        builder.Property(p => p.InvoiceNumber).HasMaxLength(BillingDocumentMapping.NumberLength).IsRequired();
        builder.Property(p => p.Status).HasConversion<int>();
        builder.Property(p => p.RequestedBy).HasMaxLength(PaymentPlan.MaxRequesterLength).IsRequired();
        builder.Ignore(p => p.FinalDueDate);
        builder.Ignore(p => p.IsAcknowledged);
        builder.IsVersioned();
        builder.HasIndex(p => new { p.InvoiceId, p.Status });
        builder.HasIndex(p => p.RequestedAt);
        builder.OwnsMany(p => p.Installments, installment =>
        {
            installment.ToTable("PaymentPlanInstallments");
            installment.WithOwner().HasForeignKey("PaymentPlanId");
            installment.HasKey("PaymentPlanId", nameof(PaymentPlanInstallment.Number));
            installment.Property(i => i.Number).ValueGeneratedNever();
        });
        builder.Navigation(p => p.Installments).HasField("_installments").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>Oppositions au ciblage selon le comportement de paiement : une ligne par client au plus.</summary>
internal sealed class PaymentTargetingObjectionConfiguration : IEntityTypeConfiguration<PaymentTargetingObjection>
{
    public void Configure(EntityTypeBuilder<PaymentTargetingObjection> builder)
    {
        builder.ToTable("PaymentTargetingObjections");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.CustomerId).HasConversion(id => id.Value, value => new CustomerId(value)).IsRequired();
        builder.HasIndex(o => o.CustomerId).IsUnique();
    }
}
