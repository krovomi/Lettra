using Lettra.Application.Abstractions.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Outbox;

/// <summary>Un message de l'outbox du serveur (table <c>OutboxMessages</c>), gardé après envoi comme trace.</summary>
internal sealed class OutboxRecord
{
    public const int KindLength = 100;
    public const int KeyLength = 200;
    public const int ErrorLength = 500;

    public Guid Id { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public long EnqueuedAtMs { get; set; }

    /// <summary>Essais commencés, compté à la prise du bail : un envoi interrompu par un arrêt compte aussi.</summary>
    public int Attempts { get; set; }

    public long NextAttemptAtMs { get; set; }

    public long? LeaseUntilMs { get; set; }

    public Guid? LeaseOwner { get; set; }

    public long? DeliveredAtMs { get; set; }

    public long? ParkedAtMs { get; set; }

    public string? LastError { get; set; }

    public OutboxMessageState ToState() => new(
        Id,
        Kind,
        IdempotencyKey,
        Attempts,
        DateTimeOffset.FromUnixTimeMilliseconds(EnqueuedAtMs),
        DateTimeOffset.FromUnixTimeMilliseconds(NextAttemptAtMs),
        DeliveredAtMs is long delivered ? DateTimeOffset.FromUnixTimeMilliseconds(delivered) : null,
        ParkedAtMs is long parked ? DateTimeOffset.FromUnixTimeMilliseconds(parked) : null,
        LastError);
}

internal sealed class OutboxRecordConfiguration : IEntityTypeConfiguration<OutboxRecord>
{
    public void Configure(EntityTypeBuilder<OutboxRecord> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Kind).HasMaxLength(OutboxRecord.KindLength).IsRequired();
        builder.Property(r => r.IdempotencyKey).HasMaxLength(OutboxRecord.KeyLength).IsRequired();
        builder.Property(r => r.Payload).IsRequired();
        builder.Property(r => r.LastError).HasMaxLength(OutboxRecord.ErrorLength);
        builder.HasIndex(r => r.IdempotencyKey).IsUnique();

        // La passe cherche les messages dus : ni partis, ni mis de côté, par date du prochain essai.
        builder.HasIndex(r => new { r.DeliveredAtMs, r.ParkedAtMs, r.NextAttemptAtMs });
    }
}
