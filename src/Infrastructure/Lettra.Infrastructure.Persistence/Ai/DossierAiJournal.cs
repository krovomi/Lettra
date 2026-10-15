using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Ai;

/// <summary>
/// Une ligne du journal IA, dans la base du dossier : elle suit donc son chiffrement, sa sauvegarde, sa rétention et son
/// effacement (annexe F §10). Le texte est déjà pseudonymisé par l'orchestrateur ; aucune vraie valeur n'est écrite ici.
/// </summary>
internal sealed class AiJournalRecord
{
    public const int MaxEngineLength = 64;

    private AiJournalRecord()
    {
    }

    public AiJournalRecord(AiAuditEntry entry)
    {
        At = entry.At;
        UserId = entry.UserId;
        Kind = entry.Kind;
        Detail = entry.Detail;
        Engine = entry.Engine is { Length: > MaxEngineLength } engine ? engine[..MaxEngineLength] : entry.Engine;
    }

    /// <summary>Ordre d'écriture : le tri se fait sur cette clé, comparable sur les trois providers (une date ne l'est pas sur SQLite).</summary>
    public long Id { get; private set; }

    public DateTimeOffset At { get; private set; }

    public Guid UserId { get; private set; }

    public AiAuditKind Kind { get; private set; }

    public string Detail { get; private set; } = string.Empty;

    public string? Engine { get; private set; }

    public AiAuditEntry ToEntry(DossierId dossier) => new(At, dossier, UserId, Kind, Detail, Engine);
}

internal sealed class AiJournalRecordConfiguration : IEntityTypeConfiguration<AiJournalRecord>
{
    public void Configure(EntityTypeBuilder<AiJournalRecord> builder)
    {
        builder.ToTable("AiJournal");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedOnAdd();
        builder.Property(r => r.Kind).HasConversion<int>();
        builder.Property(r => r.Detail).IsRequired();
        builder.Property(r => r.Engine).HasMaxLength(AiJournalRecord.MaxEngineLength);
    }
}

/// <summary>
/// Journal IA du dossier courant. Son propre DbContext (pas celui de l'unité de travail) : chaque entrée est écrite
/// tout de suite, même si la réponse est interrompue, sans jamais enregistrer au passage une modification métier en cours.
/// </summary>
internal sealed class DossierAiAuditLog(
    ITenantContext tenant,
    ITenantDbContextFactory factory,
    TenantMigrator migrator,
    PersistenceOptions options) : IAiAuditLog, IAsyncDisposable
{
    private readonly TenantDbSession _session = new(tenant, factory, migrator, options);

    public async ValueTask AppendAsync(AiAuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        EnsureCurrent(entry.Dossier);
        LettraDbContext db = await _session.GetAsync(cancellationToken).ConfigureAwait(false);
        db.AiJournal.Add(new AiJournalRecord(entry));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.Clear();
    }

    public async ValueTask<IReadOnlyList<AiAuditEntry>> ReadAsync(DossierId dossier, int limit, CancellationToken cancellationToken)
    {
        EnsureCurrent(dossier);
        LettraDbContext db = await _session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<AiJournalRecord> records = await db.AiJournal.AsNoTracking()
            .OrderByDescending(r => r.Id)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. records.Select(r => r.ToEntry(dossier))];
    }

    public ValueTask DisposeAsync() => _session.DisposeAsync();

    /// <summary>Une base par dossier : écrire ou lire le journal d'un autre dossier que celui de la requête est une erreur de routage.</summary>
    private void EnsureCurrent(DossierId dossier)
    {
        if (tenant.Current.DossierId != dossier)
        {
            throw new InvalidOperationException(
                $"Journal IA du dossier {dossier.Value} demandé dans la portée du dossier {tenant.Current.DossierId.Value}. Requête bloquée.");
        }
    }
}
