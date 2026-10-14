using Lettra.Application.Abstractions.Persistence;
using Lettra.Domain.Common;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lettra.Infrastructure.Persistence.Numbering;

/// <summary>
/// Réserve le numéro EN PREMIER (<c>UPDATE ... WHERE Version = @lue</c>) puis exécute le travail, dans une seule
/// transaction explicite. Le verrou de ligne pris par l'UPDATE sérialise les validations concurrentes du dossier ;
/// celle qui perd la course voit 0 ligne modifiée, annule et recommence. Laisser EF écrire la série et la pièce
/// dans le même <c>SaveChanges</c> produit des doublons sous concurrence (constat 4 du POC base par dossier).
/// </summary>
internal sealed class DocumentNumbering(TenantDbSession session) : IDocumentNumbering
{
    internal const int MaxAttempts = 100;

    public async Task<Result<T>> ExecuteWithNextNumberAsync<T>(
        string seriesCode,
        Func<string, CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            Attempt<T> outcome = await strategy.ExecuteAsync(
                (db, seriesCode, work),
                static (_, state, token) => TryOnceAsync(state.db, state.seriesCode, state.work, token),
                verifySucceeded: null,
                cancellationToken).ConfigureAwait(false);

            if (!outcome.LostRace)
            {
                return outcome.Result;
            }

            await Task.Delay(Random.Shared.Next(1, Math.Min(5 * attempt, 50)), cancellationToken).ConfigureAwait(false);
        }

        return NumberingErrors.Contention;
    }

    private static async Task<Attempt<T>> TryOnceAsync<T>(
        LettraDbContext db,
        string seriesCode,
        Func<string, CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var series = await db.NumberSeries.AsNoTracking()
            .Where(s => s.Code == seriesCode)
            .Select(s => new { s.Prefix, s.Next, s.Version })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (series is null)
        {
            return new Attempt<T>(NumberingErrors.SeriesNotFound, LostRace: false);
        }

        int reserved = await db.NumberSeries
            .Where(s => s.Code == seriesCode && s.Version == series.Version)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.Next, x => x.Next + 1).SetProperty(x => x.Version, x => x.Version + 1),
                cancellationToken).ConfigureAwait(false);
        if (reserved == 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new Attempt<T>(NumberingErrors.Contention, LostRace: true);
        }

        Result<T> result = await work(NumberSeries.Format(series.Prefix, series.Next), cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            // Tout ou rien : le numéro n'est pas consommé.
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            return new Attempt<T>(result, LostRace: false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new Attempt<T>(result, LostRace: false);
    }

    private sealed record Attempt<T>(Result<T> Result, bool LostRace);
}
