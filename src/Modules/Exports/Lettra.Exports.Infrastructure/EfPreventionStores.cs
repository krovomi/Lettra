using Lettra.Domain.Prevention;
using Lettra.Exports.Application.Insights.Prevention;
using Lettra.Exports.Domain.Platform;
using Lettra.Exports.Domain.Reception;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.EInvoicing;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Exports.Infrastructure;

/// <summary>Les seuils du radar de prévention (table <c>PreventionSettings</c>, une seule ligne).</summary>
internal sealed class EfPreventionSettingsStore(TenantDbSession session) : IPreventionSettingsStore
{
    public async Task<PreventionSettings?> FindAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<PreventionSettings>().FirstOrDefaultAsync(s => s.Id == PreventionSettings.SingletonId, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(PreventionSettings settings, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<PreventionSettings>().AddAsync(settings, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Les factures fournisseurs reçues encore à payer, sans leur fichier : ni avoirs, ni refusées, ni marquées payées ou
/// encaissées. Une facture sans échéance est due trente jours après son émission.
/// </summary>
internal sealed class EfPreventionPayablesReader(TenantDbSession session) : IPreventionPayablesReader
{
    private static readonly int[] Settled = [(int)PlatformStatus.Refused, (int)PlatformStatus.PaymentSent, (int)PlatformStatus.Collected];

    public async Task<IReadOnlyList<PreventionPayable>> ReadAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Set<ReceivedInvoiceRecord>()
            .AsNoTracking()
            .Where(r => r.Kind != (int)ReceivedInvoiceKind.CreditNote && !r.Events.Any(e => Settled.Contains(e.Status)))
            .Select(r => new { r.IssueDate, r.DueDate, r.TotalInclTax })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. rows.Select(r => new PreventionPayable(r.DueDate ?? r.IssueDate.AddDays(PreventionRules.DefaultSupplierTermDays), r.TotalInclTax))];
    }
}
