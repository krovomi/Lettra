using Lettra.Domain.Banking;
using Lettra.Domain.Billing;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Billing;
using Lettra.Infrastructure.Persistence.Concurrency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Banking;

/// <summary>
/// Écriture du journal de banque (BQ, vague 6) : celle d'un encaissement, ou de sa contre-passation. Comme le journal
/// des ventes, elle est écrite une fois, dans la transaction qui la produit, et le FEC la lit telle quelle.
/// </summary>
internal sealed class BankJournalRecord
{
    public Guid Id { get; set; }

    public Guid PaymentId { get; set; }

    public bool IsReversal { get; set; }

    public string PieceNumber { get; set; } = string.Empty;

    public DateOnly Date { get; set; }

    public Guid CustomerId { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public List<BankJournalLineRecord> Lines { get; } = [];
}

internal sealed class BankJournalLineRecord
{
    public Guid EntryId { get; set; }

    public int LineNumber { get; set; }

    public string Account { get; set; } = string.Empty;

    public decimal Debit { get; set; }

    public decimal Credit { get; set; }

    public decimal? VatRatePercent { get; set; }
}

/// <summary>Un IBAN déjà vu pour un client (empreinte propre au dossier), appris à la validation d'un rapprochement.</summary>
internal sealed class KnownCustomerIbanRecord
{
    public Guid CustomerId { get; set; }

    public string IbanHash { get; set; } = string.Empty;

    public long LearnedAtMs { get; set; }
}

internal sealed class BankJournalRecordConfiguration : IEntityTypeConfiguration<BankJournalRecord>
{
    public void Configure(EntityTypeBuilder<BankJournalRecord> builder)
    {
        builder.ToTable("BankJournalEntries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.PieceNumber).HasMaxLength(BillingDocumentMapping.NumberLength).IsRequired();
        builder.Property(e => e.CustomerName).HasMaxLength(200).IsRequired();
        // Un encaissement a une écriture, et au plus une contre-passation.
        builder.HasIndex(e => new { e.PaymentId, e.IsReversal }).IsUnique();
        builder.HasIndex(e => e.Date);
        builder.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.EntryId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BankJournalLineRecordConfiguration : IEntityTypeConfiguration<BankJournalLineRecord>
{
    public void Configure(EntityTypeBuilder<BankJournalLineRecord> builder)
    {
        builder.ToTable("BankJournalLines");
        builder.HasKey(l => new { l.EntryId, l.LineNumber });
        builder.Property(l => l.LineNumber).ValueGeneratedNever();
        builder.Property(l => l.Account).HasMaxLength(10).IsRequired();
    }
}

internal sealed class KnownCustomerIbanRecordConfiguration : IEntityTypeConfiguration<KnownCustomerIbanRecord>
{
    public void Configure(EntityTypeBuilder<KnownCustomerIbanRecord> builder)
    {
        builder.ToTable("KnownCustomerIbans");
        builder.HasKey(k => new { k.CustomerId, k.IbanHash });
        builder.Property(k => k.IbanHash).HasMaxLength(64);
        builder.HasIndex(k => k.IbanHash);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasConversion(id => id.Value, value => new PaymentId(value)).ValueGeneratedNever();
        builder.Property(p => p.Number).HasMaxLength(BillingDocumentMapping.NumberLength).IsRequired();
        builder.Property(p => p.CustomerId).HasConversion(id => id.Value, value => new CustomerId(value)).IsRequired();
        builder.Property(p => p.CustomerName).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Method).HasConversion<int>();
        builder.Property(p => p.Status).HasConversion<int>();
        builder.Property(p => p.Reference).HasMaxLength(Payment.MaxReferenceLength);
        builder.Property(p => p.ReversalReason).HasMaxLength(Payment.MaxReasonLength);
        builder.IsVersioned();
        builder.Ignore(p => p.AllocatedAmount);
        builder.Ignore(p => p.WriteOff);
        builder.Ignore(p => p.IsReversed);
        builder.Ignore(p => p.DomainEvents);
        builder.HasIndex(p => p.Number).IsUnique();
        builder.HasIndex(p => p.CustomerId);
        builder.HasIndex(p => p.Date);
        builder.HasIndex(p => p.BankLineId);

        builder.OwnsMany(p => p.Allocations, allocation =>
        {
            allocation.ToTable("PaymentAllocations");
            allocation.WithOwner().HasForeignKey("PaymentId");
            allocation.Property(a => a.InvoiceId).HasConversion(id => id.Value, value => new InvoiceId(value));
            allocation.HasKey("PaymentId", nameof(PaymentAllocation.InvoiceId));
            allocation.Property(a => a.InvoiceNumber).HasMaxLength(BillingDocumentMapping.NumberLength).IsRequired();
            allocation.HasIndex(a => a.InvoiceId);
        });
        builder.Navigation(p => p.Allocations).HasField("_allocations").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(p => p.VatShares, share =>
        {
            share.ToTable("PaymentVatShares");
            share.WithOwner().HasForeignKey("PaymentId");
            share.Property(s => s.InvoiceId).HasConversion(id => id.Value, value => new InvoiceId(value));
            share.HasKey("PaymentId", nameof(PaymentVatShare.InvoiceId), nameof(PaymentVatShare.VatRatePercent));
        });
        builder.Navigation(p => p.VatShares).HasField("_vatShares").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class BankStatementImportConfiguration : IEntityTypeConfiguration<BankStatementImport>
{
    public void Configure(EntityTypeBuilder<BankStatementImport> builder)
    {
        builder.ToTable("BankStatementImports");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.FileName).HasMaxLength(BankStatementImport.MaxFileNameLength).IsRequired();
        builder.Property(i => i.Format).HasConversion<int>();
        builder.Property(i => i.AccountDisplay).HasMaxLength(20);
    }
}

internal sealed class BankStatementLineConfiguration : IEntityTypeConfiguration<BankStatementLine>
{
    public void Configure(EntityTypeBuilder<BankStatementLine> builder)
    {
        builder.ToTable("BankStatementLines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Label).HasMaxLength(BankStatementLine.MaxLabelLength).IsRequired();
        builder.Property(l => l.Reference).HasMaxLength(BankStatementLine.MaxReferenceLength);
        builder.Property(l => l.CounterpartyName).HasMaxLength(BankStatementLine.MaxNameLength);
        builder.Property(l => l.CounterpartyIbanDisplay).HasMaxLength(20);
        builder.Property(l => l.CounterpartyIbanHash).HasMaxLength(64);
        builder.Property(l => l.Fingerprint).HasMaxLength(64).IsRequired();
        builder.Property(l => l.Status).HasConversion<int>();
        builder.IsVersioned();
        builder.Ignore(l => l.IsCredit);
        builder.Ignore(l => l.DomainEvents);
        // Un relevé importé deux fois ne crée pas de doublon.
        builder.HasIndex(l => l.Fingerprint).IsUnique();
        builder.HasIndex(l => l.Status);
        builder.HasIndex(l => l.ImportId);
    }
}

internal sealed class LetteringConfiguration : IEntityTypeConfiguration<Lettering>
{
    public void Configure(EntityTypeBuilder<Lettering> builder)
    {
        builder.ToTable("Letterings");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.CustomerId).HasConversion(id => id.Value, value => new CustomerId(value)).IsRequired();
        builder.Property(l => l.Code).HasMaxLength(Lettering.MaxCodeLength).IsRequired();
        builder.Property(l => l.CancelReason).HasMaxLength(Payment.MaxReasonLength);
        builder.Ignore(l => l.IsActive);
        builder.Ignore(l => l.DomainEvents);
        // Un code n'est jamais réutilisé pour un client, même après un délettrage.
        builder.HasIndex(l => new { l.CustomerId, l.Code }).IsUnique();

        builder.OwnsMany(l => l.Items, item =>
        {
            item.ToTable("LetteringItems");
            item.WithOwner().HasForeignKey("LetteringId");
            item.Property(i => i.Kind).HasConversion<int>();
            item.HasKey("LetteringId", nameof(LetteringItem.Kind), nameof(LetteringItem.DocumentId));
            item.HasIndex(i => i.DocumentId);
        });
        builder.Navigation(l => l.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class BankingSettingsConfiguration : IEntityTypeConfiguration<BankingSettings>
{
    public void Configure(EntityTypeBuilder<BankingSettings> builder)
    {
        builder.ToTable("BankingSettings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.IsVersioned();
    }
}
