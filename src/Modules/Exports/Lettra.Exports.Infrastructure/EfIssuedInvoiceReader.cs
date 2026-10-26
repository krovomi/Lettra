using Lettra.Domain.Billing;
using Lettra.Exports.Application.Insights;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Exports.Infrastructure;

/// <summary>Une pièce validée du dossier par son numéro (unique dans le dossier), sans ses lignes.</summary>
internal sealed class EfIssuedInvoiceReader(TenantDbSession session) : IIssuedInvoiceReader
{
    public async Task<IssuedInvoice?> FindByNumberAsync(string number, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<Invoice>()
            .AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Draft && i.Number == number && i.IssueDate != null)
            .Select(i => new IssuedInvoice(i.Id, i.Number!, i.IssueDate!.Value, i.TotalInclTax, i.CreditedInclTax))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
