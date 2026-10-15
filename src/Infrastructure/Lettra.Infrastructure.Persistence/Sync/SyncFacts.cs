using Lettra.Application.Mapping;
using Lettra.Calculations;
using Lettra.Contracts.Sync;
using Lettra.Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lettra.Infrastructure.Persistence.Sync;

/// <summary>
/// Sur un poste : une pièce qui vient d'être validée dans la série du poste part dans l'outbox comme un fait
/// (<see cref="ValidatedInvoiceFact"/>), dans la même transaction que sa validation. Elle ne peut donc ni être validée
/// sans partir un jour, ni partir sans avoir été validée. Le serveur, lui, ne l'enregistre pas : il reçoit ces pièces.
/// </summary>
internal sealed class SyncFactInterceptor(TimeProvider clock) : SaveChangesInterceptor, IDossierInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Record(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Record(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Record(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        List<SyncOutboxRecord> facts = [];
        foreach (EntityEntry<Invoice> entry in context.ChangeTracker.Entries<Invoice>())
        {
            Invoice invoice = entry.Entity;
            if (entry.State is not (EntityState.Added or EntityState.Modified) || invoice.DeviceId is null || invoice.ContentHash is null
                || (entry.State == EntityState.Modified && entry.Property(i => i.ContentHash).OriginalValue is not null))
            {
                // Seul le scellement (empreinte posée à la validation) fait un fait ; l'émission qui suit n'en fait pas.
                continue;
            }

            facts.Add(new SyncOutboxRecord(new ValidatedInvoiceFact(
                Guid.CreateVersion7(),
                invoice.Id.Value,
                invoice.ValidatedAt ?? clock.GetUtcNow(),
                invoice.ToResponse(),
                [.. invoice.Lines.Where(l => l.Nature == RevenueNature.DownPayment).Select(l => l.Position)],
                invoice.Customer.IsBusiness,
                invoice.LatePenaltyRate,
                invoice.EarlyPaymentDiscount)));
        }

        context.Set<SyncOutboxRecord>().AddRange(facts);
    }
}
