using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Billing;

/// <summary>Écriture du journal des ventes (VE) d'une pièce validée ; le FEC (phase 5) la lit telle quelle.</summary>
internal sealed class SalesJournalRecord
{
    public Guid Id { get; set; }

    public Guid InvoiceId { get; set; }

    public string PieceNumber { get; set; } = string.Empty;

    public DateOnly Date { get; set; }

    public Guid CustomerId { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public List<SalesJournalLineRecord> Lines { get; } = [];
}

internal sealed class SalesJournalLineRecord
{
    public Guid EntryId { get; set; }

    public int LineNumber { get; set; }

    public string Account { get; set; } = string.Empty;

    public decimal Debit { get; set; }

    public decimal Credit { get; set; }

    public decimal? VatRatePercent { get; set; }
}

internal sealed class SalesJournalRecordConfiguration : IEntityTypeConfiguration<SalesJournalRecord>
{
    public void Configure(EntityTypeBuilder<SalesJournalRecord> builder)
    {
        builder.ToTable("SalesJournalEntries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.PieceNumber).HasMaxLength(BillingDocumentMapping.NumberLength).IsRequired();
        builder.Property(e => e.CustomerName).HasMaxLength(200).IsRequired();
        // Une pièce validée n'a qu'une écriture.
        builder.HasIndex(e => e.InvoiceId).IsUnique();
        builder.HasIndex(e => e.Date);
        builder.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.EntryId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SalesJournalLineRecordConfiguration : IEntityTypeConfiguration<SalesJournalLineRecord>
{
    public void Configure(EntityTypeBuilder<SalesJournalLineRecord> builder)
    {
        builder.ToTable("SalesJournalLines");
        builder.HasKey(l => new { l.EntryId, l.LineNumber });
        builder.Property(l => l.LineNumber).ValueGeneratedNever();
        builder.Property(l => l.Account).HasMaxLength(10).IsRequired();
    }
}
