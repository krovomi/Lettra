using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.EInvoicing;

/// <summary>
/// Une facture fournisseur reçue par la plateforme agréée (module Exports) : l'en-tête lu dans le fichier, et le fichier
/// lui-même, gardé tel quel. Les instants sont en millisecondes Unix : SQLite ne trie pas les <c>DateTimeOffset</c>.
/// </summary>
internal sealed class ReceivedInvoiceRecord
{
    public Guid Id { get; set; }

    public string Platform { get; set; } = string.Empty;

    public string Reference { get; set; } = string.Empty;

    /// <summary><c>EInvoiceFormatDto</c> : 1 CII, 2 UBL.</summary>
    public int Format { get; set; }

    public string FileName { get; set; } = string.Empty;

    public byte[] Content { get; set; } = [];

    /// <summary><c>ReceivedInvoiceKindDto</c> : 1 facture, 2 acompte, 3 avoir.</summary>
    public int Kind { get; set; }

    public string Number { get; set; } = string.Empty;

    public DateOnly IssueDate { get; set; }

    public DateOnly? DueDate { get; set; }

    public string SupplierName { get; set; } = string.Empty;

    public string? SupplierSiren { get; set; }

    public string? SupplierVatNumber { get; set; }

    public Guid? SupplierId { get; set; }

    public string BuyerSiren { get; set; } = string.Empty;

    public string Currency { get; set; } = "EUR";

    public decimal TotalExclTax { get; set; }

    public decimal TotalVat { get; set; }

    public decimal TotalInclTax { get; set; }

    public string? BusinessProcess { get; set; }

    public long ReceivedAtMs { get; set; }

    public List<ReceivedInvoiceStatusRecord> Events { get; set; } = [];
}

/// <summary>Un statut du cycle de vie d'une facture reçue (code XP Z12-012), dans l'ordre de son arrivée.</summary>
internal sealed class ReceivedInvoiceStatusRecord
{
    public Guid ReceivedInvoiceId { get; set; }

    public int Sequence { get; set; }

    public int Status { get; set; }

    public long AtMs { get; set; }

    public string? Reason { get; set; }

    public decimal? Amount { get; set; }
}

internal sealed class ReceivedInvoiceRecordConfiguration : IEntityTypeConfiguration<ReceivedInvoiceRecord>
{
    public void Configure(EntityTypeBuilder<ReceivedInvoiceRecord> builder)
    {
        builder.ToTable("ReceivedInvoices");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Platform).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Reference).HasMaxLength(200).IsRequired();
        builder.Property(r => r.FileName).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Content).IsRequired();
        builder.Property(r => r.Number).HasMaxLength(100).IsRequired();
        builder.Property(r => r.SupplierName).HasMaxLength(200).IsRequired();
        builder.Property(r => r.SupplierSiren).HasMaxLength(9);
        builder.Property(r => r.SupplierVatNumber).HasMaxLength(20);
        builder.Property(r => r.BuyerSiren).HasMaxLength(9).IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.BusinessProcess).HasMaxLength(10);
        builder.HasIndex(r => r.Reference).IsUnique();
        builder.HasIndex(r => r.ReceivedAtMs);
        builder.HasMany(r => r.Events).WithOne().HasForeignKey(e => e.ReceivedInvoiceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReceivedInvoiceStatusRecordConfiguration : IEntityTypeConfiguration<ReceivedInvoiceStatusRecord>
{
    public void Configure(EntityTypeBuilder<ReceivedInvoiceStatusRecord> builder)
    {
        builder.ToTable("ReceivedInvoiceStatusEvents");
        builder.HasKey(e => new { e.ReceivedInvoiceId, e.Sequence });
        builder.Property(e => e.Reason).HasMaxLength(1000);
    }
}
