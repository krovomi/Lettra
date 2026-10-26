using Lettra.Domain.Billing;
using Lettra.Domain.Parties;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Exports.Application.Insights;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.EInvoicing;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Exports.Infrastructure;

/// <summary>
/// Ce que le score de préparation à la facture électronique lit dans la base du dossier : la fiche entreprise, les
/// clients et fournisseurs non archivés, les factures validées depuis une date et la dernière transmission de chacune.
/// Seule la présence des identifiants sort d'ici, jamais leur valeur. Lecture seule.
/// </summary>
internal sealed class EfEInvoicingReadinessReader(TenantDbSession session) : IEInvoicingReadinessReader
{
    public async Task<EInvoicingReadinessInput> ReadAsync(DateOnly since, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        CompanyProfile? profile = await db.Set<CompanyProfile>().AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        List<Customer> customers = await db.Set<Customer>().AsNoTracking().Where(c => !c.IsArchived).ToListAsync(cancellationToken).ConfigureAwait(false);
        List<Supplier> suppliers = await db.Set<Supplier>().AsNoTracking().Where(s => !s.IsArchived).ToListAsync(cancellationToken).ConfigureAwait(false);
        var invoices = await db.Set<Invoice>()
            .AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Draft && i.Number != null && i.IssueDate != null && i.IssueDate >= since)
            .Select(i => new { i.Id, i.Number, i.Customer })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        Guid[] ids = [.. invoices.Select(i => i.Id.Value)];
        List<PlatformTransmissionRecord> records = await db.Set<PlatformTransmissionRecord>()
            .AsNoTracking()
            .Include(r => r.Events)
            .Where(r => ids.Contains(r.InvoiceId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        Dictionary<Guid, PlatformTransmissionRecord> latest = records
            .GroupBy(r => r.InvoiceId)
            .ToDictionary(g => g.Key, g => EfPlatformTransmissionStore.Latest(g)!);

        return new EInvoicingReadinessInput(
            profile is null ? null : new CompanyReadinessFacts(profile.Siren is not null, profile.Siret is not null, profile.VatNumber is not null, profile.VatFranchise, profile.Iban is not null),
            [.. customers.Select(c => new PartyReadinessFacts(c.Id.Value, c.Name, c.Kind == CustomerKind.Company, c.BillingAddress.Country.Value, c.Siren is not null, c.VatNumber is not null))],
            [.. suppliers.Select(s => new PartyReadinessFacts(s.Id.Value, s.Name, IsCompany: true, s.Address.Country.Value, s.Siren is not null, s.VatNumber is not null))],
            [
                .. invoices.Select(i => new PieceReadinessFacts(
                    i.Id.Value,
                    i.Number!,
                    i.Customer,
                    latest.TryGetValue(i.Id.Value, out PlatformTransmissionRecord? record) ? EfPlatformTransmissionStore.ToTransmission(record).Status.ToDto() : null)),
            ]);
    }
}
