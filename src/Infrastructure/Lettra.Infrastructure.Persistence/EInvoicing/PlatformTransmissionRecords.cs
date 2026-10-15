using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.EInvoicing;

/// <summary>
/// Une transmission d'une pièce à la plateforme agréée (module Exports, phase 5.3). Les instants sont en millisecondes
/// Unix : SQLite ne trie pas les <c>DateTimeOffset</c>.
/// </summary>
internal sealed class PlatformTransmissionRecord
{
    public Guid Id { get; set; }

    public Guid InvoiceId { get; set; }

    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary><c>PlatformChannelDto</c> : 1 facture électronique, 2 e-reporting.</summary>
    public int Channel { get; set; }

    /// <summary><c>EInvoiceFormatDto</c> d'une facture électronique ; vide en e-reporting.</summary>
    public int? Format { get; set; }

    public string Platform { get; set; } = string.Empty;

    public string? Reference { get; set; }

    public decimal TotalInclTax { get; set; }

    public long SubmittedAtMs { get; set; }

    public List<PlatformStatusEventRecord> Events { get; set; } = [];
}

/// <summary>Un statut du cycle de vie (code XP Z12-012), dans l'ordre de son arrivée.</summary>
internal sealed class PlatformStatusEventRecord
{
    public Guid TransmissionId { get; set; }

    public int Sequence { get; set; }

    public int Status { get; set; }

    public long AtMs { get; set; }

    public string? Reason { get; set; }

    public decimal? Amount { get; set; }
}

internal sealed class PlatformTransmissionRecordConfiguration : IEntityTypeConfiguration<PlatformTransmissionRecord>
{
    public void Configure(EntityTypeBuilder<PlatformTransmissionRecord> builder)
    {
        builder.ToTable("PlatformTransmissions");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.InvoiceNumber).HasMaxLength(40).IsRequired();
        builder.Property(r => r.Platform).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Reference).HasMaxLength(200);
        builder.HasIndex(r => r.InvoiceId);
        builder.HasMany(r => r.Events).WithOne().HasForeignKey(e => e.TransmissionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PlatformStatusEventRecordConfiguration : IEntityTypeConfiguration<PlatformStatusEventRecord>
{
    public void Configure(EntityTypeBuilder<PlatformStatusEventRecord> builder)
    {
        builder.ToTable("PlatformStatusEvents");
        builder.HasKey(e => new { e.TransmissionId, e.Sequence });
        builder.Property(e => e.Reason).HasMaxLength(1000);
    }
}
