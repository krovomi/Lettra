using System.Collections.Concurrent;
using Lettra.Ai.Abstractions;

namespace Lettra.Ai.Orchestration;

/// <summary>Réglages IA par cabinet, en mémoire. Un cabinet sans réglage a l'IA désactivée.</summary>
public sealed class InMemoryAiUsagePolicy : IAiUsagePolicy
{
    private readonly ConcurrentDictionary<Guid, AiSettings> _settings = new();

    public void Set(Guid cabinetId, AiSettings settings) => _settings[cabinetId] = settings;

    public Task<AiSettings> GetSettingsAsync(Guid cabinetId, CancellationToken ct = default) =>
        Task.FromResult(_settings.GetValueOrDefault(cabinetId, AiSettings.Disabled));
}

/// <summary>Journal IA en mémoire (dans Lettra : table du dossier, chiffrée avec la clé du dossier).</summary>
public sealed class InMemoryAiAuditLog : IAiAuditLog
{
    private readonly ConcurrentQueue<AiAuditEntry> _entries = new();
    public IReadOnlyList<AiAuditEntry> Entries => _entries.ToList();

    public Task AppendAsync(AiAuditEntry entry, CancellationToken ct = default)
    {
        _entries.Enqueue(entry);
        return Task.CompletedTask;
    }
}

/// <summary>Moteurs disponibles sur ce déploiement, indexés par type.</summary>
public sealed class AiEngineFactory(IReadOnlyDictionary<AiEngineKind, IAiEngine> engines) : IAiEngineFactory
{
    public IAiEngine? Resolve(AiEngineKind kind) => kind == AiEngineKind.None ? null : engines.GetValueOrDefault(kind);
}
