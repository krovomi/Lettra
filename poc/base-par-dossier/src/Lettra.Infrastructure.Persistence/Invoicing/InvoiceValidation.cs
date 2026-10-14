using Lettra.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Persistence.Invoicing;

/// Validation d'une facture : réserve le numéro suivant de la série puis valide, dans une seule transaction.
/// La série est mise à jour EN PREMIER (UPDATE ... WHERE Version = @lue) : le verrou de ligne sérialise les
/// validations concurrentes du dossier ; celle qui perd la course voit 0 ligne modifiée, annule et recommence.
/// Ni trou (tout ou rien), ni doublon (index unique en dernier rempart).
public static class InvoiceValidation
{
    public static async Task<string> ValidateAsync(LettraDbContext db, Guid invoiceId, string seriesCode = "FAC", CancellationToken ct = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        for (var attempt = 1; ; attempt++)
        {
            var number = await strategy.ExecuteAsync(async token =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(token);

                var series = await db.NumberSeries.AsNoTracking().SingleAsync(s => s.Code == seriesCode, token);
                var reserved = await db.NumberSeries
                    .Where(s => s.Code == seriesCode && s.Version == series.Version)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.Next, x => x.Next + 1)
                        .SetProperty(x => x.Version, x => x.Version + 1), token);
                if (reserved == 0)
                {
                    await tx.RollbackAsync(token);
                    return null;
                }

                var invoice = await db.Invoices.AsTracking().Include(i => i.Lines).SingleAsync(i => i.Id == invoiceId, token);
                var value = $"{series.Prefix}{series.Next:D6}";
                invoice.Validate(value);
                await db.SaveChangesAsync(token);
                await tx.CommitAsync(token);
                return value;
            }, ct);

            if (number is not null)
                return number;
            if (attempt >= 100)
                throw new DbUpdateConcurrencyException($"Série {seriesCode} trop disputée : validation abandonnée après {attempt} essais.");
            await Task.Delay(Random.Shared.Next(1, Math.Min(5 * attempt, 50)), ct);
        }
    }
}
