using Lettra.Domain.Banking;
using Lettra.Domain.Billing;
using Lettra.Domain.Parties;
using Lettra.Exports.Application.Insights;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Exports.Infrastructure;

/// <summary>
/// Ce que le rejeu du journal complète avec la base du dossier, en trois requêtes quelle que soit sa taille : noms des
/// clients (archivés compris), imputations de tous les encaissements (contre-passés compris : le journal dit s'ils
/// l'étaient à l'instant) et reste dû aujourd'hui de chaque facture et acompte validé.
/// </summary>
internal sealed class EfDossierPresentReader(TenantDbSession session) : IDossierPresentReader
{
    public async Task<DossierPresent> ReadAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        var customers = await db.Set<Customer>()
            .AsNoTracking()
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        List<Payment> payments = await db.Set<Payment>()
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var invoices = await db.Set<Invoice>()
            .AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Draft && i.Number != null && i.Kind != InvoiceKind.CreditNote)
            .Select(i => new { i.Id, i.TotalInclTax, i.CreditedInclTax, i.PaidInclTax })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new DossierPresent(
            customers.ToDictionary(c => c.Id.Value, c => c.Name),
            payments.ToDictionary(p => p.Id.Value, p => (IReadOnlyList<PaymentShare>)[.. p.Allocations.Select(a => new PaymentShare(a.InvoiceId.Value, a.Amount))]),
            invoices.ToDictionary(i => i.Id.Value, i => i.TotalInclTax - i.CreditedInclTax - i.PaidInclTax));
    }
}
