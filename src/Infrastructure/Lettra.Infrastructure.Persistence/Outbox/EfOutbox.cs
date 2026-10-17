using System.Diagnostics;
using Lettra.Application.Abstractions.Outbox;
using Lettra.Application.Abstractions.Sync;
using Lettra.Application.Diagnostics;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Persistence.Outbox;

/// <summary>
/// L'outbox dans le contexte EF de la requête : le message n'est qu'ajouté au suivi, le <c>SaveChanges</c> du cas
/// d'usage l'écrit avec le changement métier (même contexte, même transaction).
/// </summary>
internal sealed class EfOutbox(TenantDbSession session, TimeProvider clock) : IOutbox
{
    public async Task<Guid> EnqueueAsync(string kind, string idempotencyKey, string payload, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(kind.Length, OutboxRecord.KindLength, nameof(kind));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(idempotencyKey.Length, OutboxRecord.KeyLength, nameof(idempotencyKey));

        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        OutboxRecord? staged = db.ChangeTracker.Entries<OutboxRecord>().Select(e => e.Entity).FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (staged is not null)
        {
            return staged.Id;
        }

        Guid? known = await db.Set<OutboxRecord>().AsNoTracking()
            .Where(r => r.IdempotencyKey == idempotencyKey)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (known is Guid id)
        {
            return id;
        }

        long now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        OutboxRecord record = new()
        {
            Id = Guid.CreateVersion7(),
            Kind = kind,
            IdempotencyKey = idempotencyKey,
            Payload = payload,
            EnqueuedAtMs = now,
            NextAttemptAtMs = now,
        };
        db.Add(record);
        return record.Id;
    }
}

/// <summary>
/// Le répartiteur : prise du bail par une mise à jour conditionnelle (un seul gagnant même entre plusieurs processus),
/// envoi dans une portée neuve du dossier, puis bilan sous le même bail. Un bilan dont le bail a été repris entre-temps
/// est ignoré : le message repart, et sa clé d'idempotence évite le double envoi chez le destinataire.
/// </summary>
internal sealed partial class EfOutboxDispatcher(
    TenantDbSession session,
    IDossierWorkScope work,
    IOptions<OutboxOptions> options,
    TimeProvider clock,
    ILogger<EfOutboxDispatcher> logger) : IOutboxDispatcher
{
    public async Task<OutboxPass> DispatchDueAsync(CancellationToken cancellationToken)
    {
        OutboxOptions settings = options.Value;
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        long now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        List<Guid> due = await db.Set<OutboxRecord>().AsNoTracking()
            .Where(r => r.DeliveredAtMs == null && r.ParkedAtMs == null && r.NextAttemptAtMs <= now && (r.LeaseUntilMs == null || r.LeaseUntilMs < now))
            .OrderBy(r => r.NextAttemptAtMs)
            .ThenBy(r => r.Id)
            .Select(r => r.Id)
            .Take(settings.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int delivered = 0, retried = 0, parked = 0;
        foreach (Guid id in due)
        {
            switch (await RunAsync(id, cancellationToken).ConfigureAwait(false))
            {
                case { Result: OutboxResult.Delivered }:
                    delivered++;
                    break;
                case { Result: OutboxResult.Retry }:
                    retried++;
                    break;
                case { Result: OutboxResult.Poison }:
                    parked++;
                    break;
            }
        }

        return new OutboxPass(delivered, retried, parked);
    }

    public Task<OutboxOutcome?> DispatchNowAsync(Guid messageId, CancellationToken cancellationToken) =>
        options.Value.DispatchInline ? RunAsync(messageId, cancellationToken) : Task.FromResult<OutboxOutcome?>(null);

    public async Task<OutboxMessageState?> FindAsync(Guid messageId, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        OutboxRecord? record = await db.Set<OutboxRecord>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == messageId, cancellationToken).ConfigureAwait(false);
        return record?.ToState();
    }

    public async Task<IReadOnlyList<OutboxMessageState>> ListParkedAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<OutboxRecord> records = await db.Set<OutboxRecord>().AsNoTracking()
            .Where(r => r.ParkedAtMs != null)
            .OrderBy(r => r.ParkedAtMs)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. records.Select(r => r.ToState())];
    }

    public async Task<OutboxBacklog> GetBacklogAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<OutboxRecord> pending = db.Set<OutboxRecord>().AsNoTracking().Where(r => r.DeliveredAtMs == null && r.ParkedAtMs == null);
        int count = await pending.CountAsync(cancellationToken).ConfigureAwait(false);
        long? oldest = count == 0
            ? null
            : await pending.MinAsync(r => (long?)r.EnqueuedAtMs, cancellationToken).ConfigureAwait(false);
        int parked = await db.Set<OutboxRecord>().AsNoTracking().CountAsync(r => r.ParkedAtMs != null, cancellationToken).ConfigureAwait(false);
        return new OutboxBacklog(count, parked, oldest is long ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null);
    }

    public async Task<bool> RequeueAsync(Guid messageId, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        long now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        int updated = await db.Set<OutboxRecord>()
            .Where(r => r.Id == messageId && r.DeliveredAtMs == null && (r.ParkedAtMs != null || r.NextAttemptAtMs > now)
                && (r.LeaseUntilMs == null || r.LeaseUntilMs < now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Attempts, r => r.ParkedAtMs != null ? 0 : r.Attempts)
                .SetProperty(r => r.ParkedAtMs, (long?)null)
                .SetProperty(r => r.NextAttemptAtMs, now), cancellationToken)
            .ConfigureAwait(false);
        return updated == 1;
    }

    private async Task<OutboxOutcome?> RunAsync(Guid id, CancellationToken cancellationToken)
    {
        OutboxOptions settings = options.Value;
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        long now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        long leaseUntil = now + (long)settings.Lease.TotalMilliseconds;
        Guid owner = Guid.NewGuid();
        int claimed = await db.Set<OutboxRecord>()
            .Where(r => r.Id == id && r.DeliveredAtMs == null && r.ParkedAtMs == null && r.NextAttemptAtMs <= now
                && (r.LeaseUntilMs == null || r.LeaseUntilMs < now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.LeaseUntilMs, leaseUntil)
                .SetProperty(r => r.LeaseOwner, owner)
                .SetProperty(r => r.Attempts, r => r.Attempts + 1), cancellationToken)
            .ConfigureAwait(false);
        if (claimed == 0)
        {
            return null;
        }

        OutboxRecord message = await db.Set<OutboxRecord>().AsNoTracking().FirstAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
        OutboxDelivery delivery = new(message.Id, message.Kind, message.IdempotencyKey, message.Payload, message.Attempts,
            DateTimeOffset.FromUnixTimeMilliseconds(message.EnqueuedAtMs));
        OutboxOutcome outcome;
        try
        {
            outcome = await work.RunAsync(async (provider, token) =>
            {
                IOutboxHandler? handler = provider.GetServices<IOutboxHandler>().FirstOrDefault(h => h.Kind == delivery.Kind);
                return handler is null
                    ? OutboxOutcome.Retry($"Aucun gestionnaire pour « {delivery.Kind} ».")
                    : await handler.HandleAsync(delivery, token).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            outcome = OutboxOutcome.Retry(exception.GetType().Name);
        }

        if (outcome.Result == OutboxResult.Retry && message.Attempts >= settings.MaxAttempts)
        {
            outcome = OutboxOutcome.Poison(outcome.Reason ?? "Essais épuisés.");
        }

        long done = clock.GetUtcNow().ToUnixTimeMilliseconds();
        string? reason = outcome.Reason is { Length: > OutboxRecord.ErrorLength } text ? text[..OutboxRecord.ErrorLength] : outcome.Reason;
        IQueryable<OutboxRecord> mine = db.Set<OutboxRecord>().Where(r => r.Id == id && r.LeaseOwner == owner);
        int recorded = outcome.Result switch
        {
            OutboxResult.Delivered => await mine.ExecuteUpdateAsync(s => s
                .SetProperty(r => r.DeliveredAtMs, done)
                .SetProperty(r => r.LeaseUntilMs, (long?)null)
                .SetProperty(r => r.LastError, (string?)null), cancellationToken).ConfigureAwait(false),
            OutboxResult.Retry => await mine.ExecuteUpdateAsync(s => s
                .SetProperty(r => r.NextAttemptAtMs, done + (long)settings.RetryDelay(message.Attempts).TotalMilliseconds)
                .SetProperty(r => r.LeaseUntilMs, (long?)null)
                .SetProperty(r => r.LastError, reason), cancellationToken).ConfigureAwait(false),
            _ => await mine.ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ParkedAtMs, done)
                .SetProperty(r => r.LeaseUntilMs, (long?)null)
                .SetProperty(r => r.LastError, reason), cancellationToken).ConfigureAwait(false),
        };
        if (recorded == 0)
        {
            LogLeaseLost(message.Kind, message.Id);
        }
        else if (outcome.Result == OutboxResult.Poison)
        {
            LogParked(message.Kind, message.Id, message.Attempts);
        }

        LettraTelemetry.OutboxDispatches.Add(1, new TagList
        {
            { LettraTelemetry.Attributes.OutboxKind, message.Kind },
            { LettraTelemetry.Attributes.OutboxResult, ResultName(outcome.Result) },
        });

        return outcome;
    }

    private static string ResultName(OutboxResult result) => result switch
    {
        OutboxResult.Delivered => "delivered",
        OutboxResult.Retry => "retry",
        _ => "parked",
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox : bail perdu pendant l'envoi du message {Kind} {Id}, il repartira")]
    private partial void LogLeaseLost(string kind, Guid id);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox : message {Kind} {Id} mis de côté après {Attempts} essai(s)")]
    private partial void LogParked(string kind, Guid id, int attempts);
}
