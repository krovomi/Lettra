using Lettra.Domain.Banking;
using Lettra.Domain.Parties;
using Lettra.Exports.Application.Tax;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Exports.Infrastructure;

/// <summary>
/// Bénéficiaires suivis pour la déclaration des honoraires (table <c>FeeBeneficiaries</c>), fournisseurs du dossier et
/// virements sortants des relevés (table <c>BankStatementLines</c>, reconnus par l'empreinte de l'IBAN).
/// </summary>
internal sealed class EfFeeReturnStore(TenantDbSession session) : IFeeReturnStore
{
    public async Task<IReadOnlyList<FeePayee>> ListPayeesAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<Supplier> suppliers = await db.Set<Supplier>().AsNoTracking().Where(s => !s.IsArchived).ToListAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<SupplierId, FeeBeneficiary> beneficiaries = await db.Set<FeeBeneficiary>().AsNoTracking()
            .ToDictionaryAsync(b => b.Id, cancellationToken).ConfigureAwait(false);
        return
        [
            .. suppliers.Select(s => new FeePayee(
                s.Id,
                s.Name,
                s.Siret?.Value,
                string.Join(", ", new[] { s.Address.Line1, s.Address.Line2, $"{s.Address.PostalCode} {s.Address.City}" }.Where(p => !string.IsNullOrWhiteSpace(p))),
                s.Address.Country.Value,
                s.Iban?.Value,
                beneficiaries.GetValueOrDefault(s.Id))),
        ];
    }

    public async Task<FeeBeneficiary?> FindAsync(SupplierId supplier, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<FeeBeneficiary>().AsTracking().FirstOrDefaultAsync(b => b.Id == supplier, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(FeeBeneficiary beneficiary, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<FeeBeneficiary>().AddAsync(beneficiary, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(FeeBeneficiary beneficiary, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        db.Set<FeeBeneficiary>().Remove(beneficiary);
    }

    public async Task<bool> AnyBeneficiaryAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<FeeBeneficiary>().AsNoTracking().AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IbanDebit>> DebitsAsync(
        IReadOnlyCollection<string> ibanHashes, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ibanHashes);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        string[] hashes = [.. ibanHashes];

        // Les montants se lisent ligne par ligne : SQLite ne fait pas la somme des décimaux, l'appelant additionne.
        var debits = await db.Set<BankStatementLine>()
            .AsNoTracking()
            .Where(l => l.BookingDate >= from && l.BookingDate <= to && l.Amount < 0m && l.CounterpartyIbanHash != null && hashes.Contains(l.CounterpartyIbanHash))
            .Select(l => new { Hash = l.CounterpartyIbanHash!, l.BookingDate, l.Amount })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. debits.Select(d => new IbanDebit(d.Hash, d.BookingDate, -d.Amount))];
    }

    public async Task<DateOnly?> LastBankLineAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<BankStatementLine>().AsNoTracking().MaxAsync(l => (DateOnly?)l.BookingDate, cancellationToken).ConfigureAwait(false);
    }
}
