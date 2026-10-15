using System.Text.Json;
using Lettra.Application.Abstractions.Sync;
using Lettra.Contracts.Serialization;
using Lettra.Contracts.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Sync;

/// <summary>
/// Une intention de l'outbox du poste (ADR-0007, ADR-0008), écrite dans la transaction de sa commande. Le contenu n'est
/// jamais réécrit après coup : seule la réponse du serveur s'y ajoute (constat 4 du POC).
/// </summary>
internal sealed class SyncOutboxRecord
{
    private SyncOutboxRecord()
    {
    }

    public SyncOutboxRecord(SyncIntent intent)
    {
        IdempotencyKey = intent.IdempotencyKey;
        Entity = intent.EntityOf();
        EntityId = intent.EntityId;
        Payload = JsonSerializer.Serialize(intent, ContractsJsonContext.Default.SyncIntent);
        Status = SyncOutboxStatus.Pending;
        RecordedAtMs = intent.RecordedAt.ToUnixTimeMilliseconds();
    }

    /// <summary>Ordre de saisie : les intentions partent dans cet ordre.</summary>
    public long Sequence { get; private set; }

    public Guid IdempotencyKey { get; private set; }

    public SyncEntityKind Entity { get; private set; }

    public Guid EntityId { get; private set; }

    public string Payload { get; private set; } = string.Empty;

    public SyncOutboxStatus Status { get; private set; }

    /// <summary>Codes d'erreur du serveur pour une intention refusée.</summary>
    public string? Detail { get; private set; }

    /// <summary>En millisecondes depuis 1970 : comparable sur les trois providers (une date ne l'est pas sur SQLite).</summary>
    public long RecordedAtMs { get; private set; }

    public long? AcknowledgedAtMs { get; private set; }

    public SyncIntent Intent => JsonSerializer.Deserialize(Payload, ContractsJsonContext.Default.SyncIntent)!;

    public void Acknowledge(SyncOutboxStatus status, string? detail, DateTimeOffset now)
    {
        Status = status;
        Detail = detail;
        AcknowledgedAtMs = now.ToUnixTimeMilliseconds();
    }
}

/// <summary>Un champ disputé reçu du serveur, gardé sur le poste jusqu'à sa résolution.</summary>
internal sealed class SyncConflictRow
{
    private SyncConflictRow()
    {
    }

    public SyncConflictRow(SyncIntentResult result, SyncEntityKind entity, SyncFieldConflict conflict, DateTimeOffset now)
    {
        IdempotencyKey = result.IdempotencyKey;
        Entity = entity;
        EntityId = result.EntityId;
        Field = conflict.Field;
        BaseValue = conflict.Base.GetRawText();
        MineValue = conflict.Mine.GetRawText();
        ServerValue = conflict.Server.GetRawText();
        DetectedAtMs = now.ToUnixTimeMilliseconds();
    }

    public long Id { get; private set; }

    public Guid IdempotencyKey { get; private set; }

    public SyncEntityKind Entity { get; private set; }

    public Guid EntityId { get; private set; }

    public string Field { get; private set; } = string.Empty;

    public string BaseValue { get; private set; } = "null";

    public string MineValue { get; private set; } = "null";

    public string ServerValue { get; private set; } = "null";

    public long DetectedAtMs { get; private set; }

    public SyncConflictRecord ToRecord() => new(
        Id,
        IdempotencyKey,
        Entity,
        EntityId,
        new SyncFieldConflict(Field, Parse(BaseValue), Parse(MineValue), Parse(ServerValue)),
        DateTimeOffset.FromUnixTimeMilliseconds(DetectedAtMs));

    private static JsonElement Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

/// <summary>Petites valeurs d'état de la synchronisation du poste (jeton de réception).</summary>
internal sealed class SyncStateRecord
{
    public const string TokenKey = "changes.token";

    private SyncStateRecord()
    {
    }

    public SyncStateRecord(string key, string value)
    {
        Key = key;
        Value = value;
    }

    public string Key { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    public void Set(string value) => Value = value;
}

/// <summary>
/// Journal de synchronisation du serveur : une ligne par enregistrement d'un élément synchronisé, écrite dans la même
/// transaction. Les postes reçoivent les éléments dont une ligne dépasse leur jeton.
/// </summary>
internal sealed class SyncJournalRecord
{
    private SyncJournalRecord()
    {
    }

    public SyncJournalRecord(SyncEntityKind entity, Guid entityId, DateTimeOffset now)
    {
        Entity = entity;
        EntityId = entityId;
        ChangedAtMs = now.ToUnixTimeMilliseconds();
    }

    public long Sequence { get; private set; }

    public SyncEntityKind Entity { get; private set; }

    public Guid EntityId { get; private set; }

    public long ChangedAtMs { get; private set; }
}

/// <summary>Réponse donnée pour une clé d'idempotence, côté serveur.</summary>
internal sealed class SyncReceiptRecord
{
    private SyncReceiptRecord()
    {
    }

    public SyncReceiptRecord(SyncReceipt receipt)
    {
        IdempotencyKey = receipt.IdempotencyKey;
        DeviceId = receipt.DeviceId;
        RequestHash = receipt.RequestHash;
        Result = JsonSerializer.Serialize(receipt.Result, ContractsJsonContext.Default.SyncIntentResult);
        ReceivedAtMs = receipt.ReceivedAt.ToUnixTimeMilliseconds();
    }

    public Guid IdempotencyKey { get; private set; }

    public Guid DeviceId { get; private set; }

    public string RequestHash { get; private set; } = string.Empty;

    public string Result { get; private set; } = string.Empty;

    public long ReceivedAtMs { get; private set; }

    public SyncReceipt ToReceipt() => new(
        IdempotencyKey,
        DeviceId,
        RequestHash,
        JsonSerializer.Deserialize(Result, ContractsJsonContext.Default.SyncIntentResult)!,
        DateTimeOffset.FromUnixTimeMilliseconds(ReceivedAtMs));
}

internal sealed class SyncOutboxRecordConfiguration : IEntityTypeConfiguration<SyncOutboxRecord>
{
    public void Configure(EntityTypeBuilder<SyncOutboxRecord> builder)
    {
        builder.ToTable("SyncOutbox");
        builder.HasKey(r => r.Sequence);
        builder.Property(r => r.Sequence).ValueGeneratedOnAdd();
        builder.HasIndex(r => r.IdempotencyKey).IsUnique();
        builder.HasIndex(r => new { r.Status, r.Entity, r.EntityId });
        builder.Property(r => r.Entity).HasConversion<int>();
        builder.Property(r => r.Status).HasConversion<int>();
        builder.Property(r => r.Payload).IsRequired();
        builder.Property(r => r.Detail).HasMaxLength(1000);
        builder.Ignore(r => r.Intent);
    }
}

internal sealed class SyncConflictRowConfiguration : IEntityTypeConfiguration<SyncConflictRow>
{
    public void Configure(EntityTypeBuilder<SyncConflictRow> builder)
    {
        builder.ToTable("SyncConflicts");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedOnAdd();
        builder.Property(r => r.Entity).HasConversion<int>();
        builder.Property(r => r.Field).HasMaxLength(100).IsRequired();
        builder.Property(r => r.BaseValue).IsRequired();
        builder.Property(r => r.MineValue).IsRequired();
        builder.Property(r => r.ServerValue).IsRequired();
        builder.HasIndex(r => new { r.Entity, r.EntityId });
    }
}

internal sealed class SyncStateRecordConfiguration : IEntityTypeConfiguration<SyncStateRecord>
{
    public void Configure(EntityTypeBuilder<SyncStateRecord> builder)
    {
        builder.ToTable("SyncState");
        builder.HasKey(r => r.Key);
        builder.Property(r => r.Key).HasMaxLength(64);
        builder.Property(r => r.Value).HasMaxLength(200).IsRequired();
    }
}

internal sealed class SyncJournalRecordConfiguration : IEntityTypeConfiguration<SyncJournalRecord>
{
    public void Configure(EntityTypeBuilder<SyncJournalRecord> builder)
    {
        builder.ToTable("SyncJournal");
        builder.HasKey(r => r.Sequence);
        builder.Property(r => r.Sequence).ValueGeneratedOnAdd();
        builder.Property(r => r.Entity).HasConversion<int>();
        builder.HasIndex(r => r.ChangedAtMs);
    }
}

internal sealed class SyncReceiptRecordConfiguration : IEntityTypeConfiguration<SyncReceiptRecord>
{
    public void Configure(EntityTypeBuilder<SyncReceiptRecord> builder)
    {
        builder.ToTable("SyncReceipts");
        builder.HasKey(r => r.IdempotencyKey);
        builder.Property(r => r.IdempotencyKey).ValueGeneratedNever();
        builder.Property(r => r.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Result).IsRequired();
    }
}
