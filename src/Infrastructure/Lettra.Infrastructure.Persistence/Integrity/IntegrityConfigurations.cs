using Lettra.Domain.Integrity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Lettra.Infrastructure.Persistence.Integrity;

/// <summary>
/// Tête de la chaîne du dossier : une seule ligne, verrouillée par un <c>UPDATE</c> conditionnel au début de chaque
/// ajout (même méthode que la numérotation), ce qui sérialise les ajouts concurrents du dossier.
/// </summary>
internal sealed class IntegrityChainHead
{
    public const int SingletonId = 1;

    public int Id { get; private set; } = SingletonId;

    public long LastSequence { get; private set; }

    /// <summary>Empreinte de la dernière entrée ; vide tant que le journal est vide (l'empreinte de départ dépend du dossier).</summary>
    public string LastHash { get; private set; } = string.Empty;

    public long Version { get; private set; }
}

/// <summary>
/// Une version du registre des clés de la chaîne du dossier : clé publique en clair (registre), secrets enveloppés par
/// la KEK du cabinet. Ajout seul ; seule l'enveloppe peut être réécrite (ré-enveloppement à la rotation de la KEK).
/// </summary>
internal sealed class StoredIntegrityKey
{
    public const int MaxPublicKeyLength = 256;
    public const int MaxWrappedLength = 128;

    public StoredIntegrityKey(int version, string keyId, string publicKey, byte[] wrappedSecret, DateTimeOffset createdAt)
    {
        Version = version;
        KeyId = keyId;
        PublicKey = publicKey;
        WrappedSecret = wrappedSecret;
        CreatedAt = createdAt;
    }

    public int Version { get; private set; }

    public string KeyId { get; private set; }

    public string PublicKey { get; private set; }

    public byte[] WrappedSecret { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>Instants en millisecondes UTC : comparables en SQL sur les trois providers, et sans perte d'un provider à l'autre.</summary>
internal static class UnixMilliseconds
{
    public static readonly ValueConverter<DateTimeOffset, long> Converter =
        new(v => v.ToUnixTimeMilliseconds(), v => DateTimeOffset.FromUnixTimeMilliseconds(v));
}

internal sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.ToTable("IntegrityJournal");
        builder.HasKey(e => e.Sequence);
        builder.Property(e => e.Sequence).ValueGeneratedNever();
        builder.Property(e => e.OccurredAt).HasConversion(UnixMilliseconds.Converter);
        builder.Property(e => e.Kind).HasConversion<int>();
        builder.Property(e => e.DocumentKind).HasConversion<int?>();
        builder.Property(e => e.SeriesCode).HasMaxLength(JournalEntry.MaxSeriesLength);
        builder.Property(e => e.Number).HasMaxLength(JournalEntry.MaxNumberLength);
        builder.Property(e => e.Currency).HasMaxLength(3);
        builder.Property(e => e.ContentHash).HasMaxLength(JournalEntry.HashLength).IsRequired();
        builder.Property(e => e.PreviousHash).HasMaxLength(JournalEntry.HashLength).IsRequired();
        builder.Property(e => e.Hash).HasMaxLength(JournalEntry.HashLength).IsRequired();
        builder.Property(e => e.Seal).HasMaxLength(JournalEntry.HashLength).IsRequired();
        builder.Property(e => e.KeyId).HasMaxLength(JournalEntry.MaxKeyIdLength);
        builder.HasIndex(e => e.DocumentId);
        builder.HasIndex(e => e.OccurredAt);
    }
}

internal sealed class PeriodClosingConfiguration : IEntityTypeConfiguration<PeriodClosing>
{
    public void Configure(EntityTypeBuilder<PeriodClosing> builder)
    {
        builder.ToTable("IntegrityClosings");
        builder.HasKey(c => c.Number);
        builder.Property(c => c.Number).ValueGeneratedNever();
        builder.Property(c => c.PeriodKind).HasConversion<int>();
        builder.Property(c => c.ClosedAt).HasConversion(UnixMilliseconds.Converter);
        builder.Property(c => c.LastEntryHash).HasMaxLength(JournalEntry.HashLength).IsRequired();
        builder.Property(c => c.PreviousClosingHash).HasMaxLength(JournalEntry.HashLength).IsRequired();
        builder.Property(c => c.Hash).HasMaxLength(JournalEntry.HashLength).IsRequired();
        builder.Property(c => c.KeyId).HasMaxLength(32).IsRequired();
        builder.Property(c => c.PublicKey).HasMaxLength(256).IsRequired();
        builder.Property(c => c.Signature).HasMaxLength(256).IsRequired();
        builder.Ignore(c => c.EntryCount);
    }
}

internal sealed class IntegrityChainHeadConfiguration : IEntityTypeConfiguration<IntegrityChainHead>
{
    public void Configure(EntityTypeBuilder<IntegrityChainHead> builder)
    {
        builder.ToTable("IntegrityChainHead");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();
        builder.Property(h => h.LastHash).HasMaxLength(JournalEntry.HashLength).IsRequired();
        builder.Property(h => h.Version).IsConcurrencyToken();

        // La ligne naît avec la migration : aucun ajout n'a à la créer, donc pas de course à sa création.
        builder.HasData(new { Id = IntegrityChainHead.SingletonId, LastSequence = 0L, LastHash = string.Empty, Version = 0L });
    }
}

internal sealed class StoredIntegrityKeyConfiguration : IEntityTypeConfiguration<StoredIntegrityKey>
{
    public void Configure(EntityTypeBuilder<StoredIntegrityKey> builder)
    {
        builder.ToTable("IntegrityKeys");
        builder.HasKey(k => k.Version);
        builder.Property(k => k.Version).ValueGeneratedNever();
        builder.Property(k => k.KeyId).HasMaxLength(JournalEntry.MaxKeyIdLength).IsRequired();
        builder.Property(k => k.PublicKey).HasMaxLength(StoredIntegrityKey.MaxPublicKeyLength).IsRequired();
        builder.Property(k => k.WrappedSecret).HasMaxLength(StoredIntegrityKey.MaxWrappedLength).IsRequired();
        builder.Property(k => k.CreatedAt).HasConversion(UnixMilliseconds.Converter);
        builder.HasIndex(k => k.KeyId).IsUnique();
    }
}
