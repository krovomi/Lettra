using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Infrastructure.Persistence.Supervision;
using Lettra.Supervision.Application;
using Lettra.Supervision.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Supervision.Infrastructure;

/// <summary>
/// Alertes dans la base de supervision. Une alerte relue garde sa version : l'écriture qui suit est conditionnelle,
/// et deux instances (API, Worker) ne s'écrasent pas sans le voir. L'empreinte est unique : une même règle sur une même
/// portée ne crée qu'une alerte, même si deux instances la déclenchent ensemble.
/// </summary>
internal sealed class EfAlertStore(IDbContextFactory<SupervisionDbContext> factory) : IAlertStore
{
    private readonly ConcurrentDictionary<AlertId, int> _versions = new();

    public async Task<Alert?> FindByFingerprintAsync(string fingerprint, CancellationToken cancellationToken)
    {
        SupervisionDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            SupervisionAlertRow? row = await db.Alerts.AsNoTracking()
                .SingleOrDefaultAsync(a => a.Fingerprint == fingerprint, cancellationToken).ConfigureAwait(false);
            return Load(row);
        }
    }

    public async Task<Alert?> GetAsync(AlertId id, CancellationToken cancellationToken)
    {
        SupervisionDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            SupervisionAlertRow? row = await db.Alerts.AsNoTracking()
                .SingleOrDefaultAsync(a => a.Id == id.Value, cancellationToken).ConfigureAwait(false);
            return Load(row);
        }
    }

    public async Task<IReadOnlyList<Alert>> ListActiveAsync(CancellationToken cancellationToken)
    {
        SupervisionDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            int closed = (int)AlertState.Closed;
            List<SupervisionAlertRow> rows = await db.Alerts.AsNoTracking()
                .Where(a => a.State != closed)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            return [.. rows.Select(r => Load(r)!)];
        }
    }

    public async Task<IReadOnlyList<Alert>> ListWithinAsync(AuthorizationScope scope, bool includeClosed, int limit, CancellationToken cancellationToken)
    {
        SupervisionDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            IQueryable<SupervisionAlertRow> query = db.Alerts.AsNoTracking();
            Guid? id = scope.Id;
            int cell = (int)ScopeLevel.Cell;
            query = scope.Kind switch
            {
                ScopeKind.Cabinet => query.Where(a => a.CabinetId == id),
                ScopeKind.Groupe => query.Where(a => a.GroupId == id),
                ScopeKind.Dossier => query.Where(a => a.DossierId == id),
                ScopeKind.Plateforme => query.Where(a => a.ScopeLevel <= cell),
                _ => query.Where(_ => false),
            };
            if (!includeClosed)
            {
                int closed = (int)AlertState.Closed;
                query = query.Where(a => a.State != closed);
            }

            List<SupervisionAlertRow> rows = await query
                .OrderByDescending(a => a.LastSeen)
                .Take(limit)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            return [.. rows.Select(r => Load(r)!)];
        }
    }

    public async Task SaveAsync(Alert alert, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alert);
        SupervisionDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            SupervisionAlertRow row = ToRow(alert);
            if (_versions.TryGetValue(alert.Id, out int version))
            {
                int updated = await db.Alerts
                    .Where(a => a.Id == row.Id && a.Version == version)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(a => a.Severity, row.Severity)
                            .SetProperty(a => a.State, row.State)
                            .SetProperty(a => a.LastSeen, row.LastSeen)
                            .SetProperty(a => a.Snapshot, row.Snapshot)
                            .SetProperty(a => a.Version, version + 1),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (updated == 0)
                {
                    throw new ConcurrencyConflictException($"L'alerte {alert.Id} a été modifiée par ailleurs.");
                }

                _versions[alert.Id] = version + 1;
                return;
            }

            row.Version = 1;
            db.Alerts.Add(row);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _versions[alert.Id] = 1;
        }
    }

    private Alert? Load(SupervisionAlertRow? row)
    {
        if (row is null)
        {
            return null;
        }

        Alert alert = Alert.Restore(JsonSerializer.Deserialize(row.Snapshot, SupervisionJsonContext.Default.Snapshot)!);
        _versions[alert.Id] = row.Version;
        return alert;
    }

    private static SupervisionAlertRow ToRow(Alert alert)
    {
        Alert.Snapshot snapshot = alert.ToSnapshot();
        return new SupervisionAlertRow
        {
            Id = snapshot.Id,
            Fingerprint = alert.Fingerprint,
            RuleId = snapshot.RuleId,
            Severity = (int)snapshot.Severity,
            State = (int)snapshot.State,
            ScopeLevel = (int)snapshot.ScopeLevel,
            CabinetId = snapshot.Cabinet,
            GroupId = snapshot.Group,
            DossierId = snapshot.Dossier,
            LastSeen = snapshot.LastSeen.ToUnixTimeMilliseconds(),
            Snapshot = JsonSerializer.Serialize(snapshot, SupervisionJsonContext.Default.Snapshot),
        };
    }
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(Alert.Snapshot))]
internal sealed partial class SupervisionJsonContext : JsonSerializerContext;

/// <summary>Migration de la base de supervision au démarrage de l'hôte (ou par le Worker en SaaS).</summary>
public sealed class SupervisionDatabaseInitializer(IDbContextFactory<SupervisionDbContext> factory)
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        SupervisionDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
