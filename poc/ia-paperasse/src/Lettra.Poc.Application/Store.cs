using System.Collections.Concurrent;

namespace Lettra.Poc.Application;

public interface IClock
{
    DateTimeOffset Now { get; }
    DateOnly Today => DateOnly.FromDateTime(Now.UtcDateTime);
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow;
}

public sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset Now => now;
}

/// <summary>Stockage en mémoire des dossiers du POC (remplace la base par dossier du socle).</summary>
public sealed class InMemoryDossierStore
{
    private readonly ConcurrentDictionary<Guid, Dossier> _dossiers = new();
    private readonly ConcurrentQueue<AuditRecord> _audit = new();

    public void Add(Dossier dossier) => _dossiers[dossier.Id] = dossier;

    public Dossier Get(Guid dossierId) =>
        _dossiers.TryGetValue(dossierId, out var d) ? d : throw new DossierNotFoundException(dossierId);

    public void Audit(AuditRecord record) => _audit.Enqueue(record);

    public IReadOnlyList<AuditRecord> AuditTrail => _audit.ToList();
}

public sealed class DossierNotFoundException(Guid id) : Exception($"Dossier {id} introuvable.");
public sealed class DocumentNotFoundException(Guid id) : Exception($"Document {id} introuvable dans ce dossier.");

/// <summary>Une action réservée à un humain a été tentée par l'assistant IA.</summary>
public sealed class HumanActionRequiredException(string action)
    : Exception($"« {action} » est réservé à un utilisateur : l'assistant IA ne valide, n'émet ni ne supprime aucun document.");
