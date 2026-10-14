using System.Collections.Concurrent;
using Lettra.Application.Abstractions.Ai;
using Lettra.Contracts.Assistant;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lettra.Ai.Orchestration;

/// <summary>
/// Réglages IA lus dans la configuration (<c>Lettra:Ai</c>). Valeur par défaut : désactivé. Les réglages propres à chaque
/// cabinet viendront de la base Catalog (modes 2, 3 et 5) ; ce fichier sert le cabinet unique des modes 1 et 2 et le développement.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Lettra:Ai";

    public bool Enabled { get; set; }

    public AiEngineKind Engine { get; set; } = AiEngineKind.None;

    public IList<AssistantFeature> Features { get; } = [];

    public IList<Guid> ExcludedDossiers { get; } = [];

    public bool OfficialWebLookup { get; set; }

    public int MaxToolRounds { get; set; } = 8;

    /// <summary>
    /// Réglages propres à un cabinet (clé : identifiant du cabinet), qui remplacent les réglages ci-dessus pour ses dossiers.
    /// Clé texte : le binder de configuration ne sait pas lire une clé <see cref="Guid"/>.
    /// </summary>
    public IDictionary<string, AiOptions> Organisations { get; } = new Dictionary<string, AiOptions>(StringComparer.OrdinalIgnoreCase);

    internal AiSettings ToSettings() => new()
    {
        Enabled = Enabled,
        Engine = Engine,
        Features = Features.ToHashSet(),
        ExcludedDossiers = ExcludedDossiers.Select(DossierId.From).ToHashSet(),
        OfficialWebLookup = OfficialWebLookup,
        MaxToolRounds = Math.Clamp(MaxToolRounds, 1, 20),
    };
}

internal sealed class ConfigurationAiUsagePolicy(IOptionsMonitor<AiOptions> options) : IAiUsagePolicy
{
    public ValueTask<AiSettings> GetSettingsAsync(OrganisationId organisation, CancellationToken cancellationToken)
    {
        AiOptions current = options.CurrentValue;
        AiOptions effective = current.Organisations.TryGetValue(organisation.Value.ToString("D"), out AiOptions? specific) ? specific : current;
        return ValueTask.FromResult(effective.ToSettings());
    }
}

/// <summary>
/// Journal IA en mémoire, borné par dossier. À remplacer par une table de la base du dossier (chiffrée avec sa clé)
/// quand la persistance de la phase 2 sera fusionnée : le port ne changera pas.
/// </summary>
internal sealed class InMemoryAiAuditLog : IAiAuditLog
{
    private const int MaxEntriesPerDossier = 5_000;

    private readonly ConcurrentDictionary<DossierId, ConcurrentQueue<AiAuditEntry>> _entries = new();

    public ValueTask AppendAsync(AiAuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ConcurrentQueue<AiAuditEntry> queue = _entries.GetOrAdd(entry.Dossier, _ => new ConcurrentQueue<AiAuditEntry>());
        queue.Enqueue(entry);
        while (queue.Count > MaxEntriesPerDossier && queue.TryDequeue(out _))
        {
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<AiAuditEntry>> ReadAsync(DossierId dossier, int limit, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<AiAuditEntry>>(
            _entries.TryGetValue(dossier, out ConcurrentQueue<AiAuditEntry>? queue)
                ? [.. queue.Reverse().Take(limit)]
                : []);
}

/// <summary>Moteurs disponibles sur ce déploiement : chaque adapter s'enregistre sous la clé de son <see cref="AiEngineKind"/>.</summary>
internal sealed class KeyedAiEngineFactory(IServiceProvider services) : IAiEngineFactory
{
    public IAiEngine? Resolve(AiEngineKind kind) =>
        kind == AiEngineKind.None ? null : services.GetKeyedService<IAiEngine>(kind);
}
