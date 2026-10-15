using Lettra.Contracts.Presence;

namespace Lettra.Api.Presence;

/// <summary>Identifie un document suivi : le dossier en fait partie, deux dossiers ne partagent jamais un groupe.</summary>
public readonly record struct PresenceKey(Guid DossierId, PresenceDocumentType DocumentType, Guid DocumentId)
{
    public string Group => $"presence:{DossierId:N}:{(int)DocumentType}:{DocumentId:N}";
}

/// <summary>Présence par connexion SignalR. Une connexion silencieuse plus longtemps que le délai d'expiration disparaît.</summary>
public interface IPresenceTracker
{
    DocumentPresence Join(PresenceKey key, string connectionId, Guid userId, string? displayName, PresenceIntent intent);

    DocumentPresence Leave(PresenceKey key, string connectionId);

    /// <summary>Retire la connexion de tous ses documents ; renvoie la nouvelle présence de chacun.</summary>
    IReadOnlyList<DocumentPresence> Disconnect(string connectionId);

    DocumentPresence Get(PresenceKey key);
}

/// <summary>
/// Présence en mémoire de l'instance. Avec plusieurs pods, les connexions d'un même document doivent arriver au même
/// pod (affinité) tant que le fond de panier Redis de SignalR n'est pas branché.
/// </summary>
internal sealed class InMemoryPresenceTracker(TimeProvider clock) : IPresenceTracker
{
    /// <summary>Trois signes de vie manqués : la connexion est considérée partie (onglet gelé, réseau coupé).</summary>
    public static readonly TimeSpan Expiry = TimeSpan.FromSeconds(PresenceHubContract.HeartbeatSeconds * 3);

    private readonly Lock _gate = new();
    private readonly Dictionary<PresenceKey, Dictionary<string, Connection>> _documents = [];

    public DocumentPresence Join(PresenceKey key, string connectionId, Guid userId, string? displayName, PresenceIntent intent)
    {
        DateTimeOffset now = clock.GetUtcNow();
        lock (_gate)
        {
            if (!_documents.TryGetValue(key, out Dictionary<string, Connection>? connections))
            {
                connections = new(StringComparer.Ordinal);
                _documents[key] = connections;
            }

            DateTimeOffset since = connections.TryGetValue(connectionId, out Connection? existing) ? existing.Since : now;
            connections[connectionId] = new Connection(userId, displayName, intent, since, now);
            return Snapshot(key, now);
        }
    }

    public DocumentPresence Leave(PresenceKey key, string connectionId)
    {
        lock (_gate)
        {
            if (_documents.TryGetValue(key, out Dictionary<string, Connection>? connections))
            {
                connections.Remove(connectionId);
            }

            return Snapshot(key, clock.GetUtcNow());
        }
    }

    public IReadOnlyList<DocumentPresence> Disconnect(string connectionId)
    {
        DateTimeOffset now = clock.GetUtcNow();
        lock (_gate)
        {
            List<DocumentPresence> changed = [];
            foreach (PresenceKey key in _documents.Where(d => d.Value.ContainsKey(connectionId)).Select(d => d.Key).ToList())
            {
                _documents[key].Remove(connectionId);
                changed.Add(Snapshot(key, now));
            }

            return changed;
        }
    }

    public DocumentPresence Get(PresenceKey key)
    {
        lock (_gate)
        {
            return Snapshot(key, clock.GetUtcNow());
        }
    }

    /// <summary>Purge les connexions expirées, puis une entrée par utilisateur : l'intention la plus forte, l'arrivée la plus ancienne.</summary>
    private DocumentPresence Snapshot(PresenceKey key, DateTimeOffset now)
    {
        List<PresenceEntry> users = [];
        if (_documents.TryGetValue(key, out Dictionary<string, Connection>? connections))
        {
            foreach (string expired in connections.Where(c => now - c.Value.LastSeen > Expiry).Select(c => c.Key).ToList())
            {
                connections.Remove(expired);
            }

            if (connections.Count == 0)
            {
                _documents.Remove(key);
            }

            users = connections.Values
                .GroupBy(c => c.UserId)
                .Select(g => new PresenceEntry(g.Key, g.First().DisplayName, g.Max(c => c.Intent), g.Min(c => c.Since)))
                .OrderBy(e => e.Since)
                .ToList();
        }

        return new DocumentPresence(key.DossierId, key.DocumentType, key.DocumentId, users);
    }

    private sealed record Connection(Guid UserId, string? DisplayName, PresenceIntent Intent, DateTimeOffset Since, DateTimeOffset LastSeen);
}
