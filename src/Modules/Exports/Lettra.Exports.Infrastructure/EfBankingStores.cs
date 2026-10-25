using Lettra.Calculations;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;
using Lettra.Domain.Banking;
using Lettra.Domain.Billing;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Banking;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Banking;
using Lettra.Infrastructure.Persistence.Billing;
using Lettra.Infrastructure.Persistence.Repositories;
using Lettra.Infrastructure.Persistence.Search;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Exports.Infrastructure;

// Encaissements, relevés et lettrages dans la base du dossier (vague 6). La liste des encaissements est cherchée, triée
// et paginée en base (montants en réel). SQLite ne trie ni ne compare les dates avec fuseau ni les décimaux en SQL : les
// autres tris et sommes se font en mémoire, sur des volumes de petite entreprise.

internal sealed class EfPaymentRepository(TenantDbSession session) : IPaymentRepository
{
    private const int BatchSize = 1000;

    public async Task AddAsync(Payment payment, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<Payment>().AddAsync(payment, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Payment?> FindAsync(PaymentId id, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<Payment>().FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Payment>> ListActiveForCustomerAsync(CustomerId customer, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<Payment>()
            .AsNoTracking()
            .Where(p => p.CustomerId == customer && p.Status == PaymentStatus.Posted)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<(IReadOnlyList<Payment> Items, int Total)> ListAsync(PaymentListFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<Payment> query = db.Set<Payment>().AsNoTracking();
        if (filter.From is DateOnly from)
        {
            query = query.Where(p => p.Date >= from);
        }

        if (filter.To is DateOnly to)
        {
            query = query.Where(p => p.Date <= to);
        }

        if (filter.Customer is CustomerId customer)
        {
            query = query.Where(p => p.CustomerId == customer);
        }

        if (ListSearchTerm.From(filter.Search) is { } term)
        {
#pragma warning disable CA1304, CA1311, CA1862 // Arbre d'expression traduit en UPPER(...) SQL : jamais exécuté en .NET.
            query = query.Where(p => EF.Property<string>(p, SearchKey.PropertyName).Contains(term.Text)
                || p.Number.ToUpper().Contains(term.Code)
                || (p.Reference ?? string.Empty).ToUpper().Contains(term.Code)
                || p.Allocations.Any(a => a.InvoiceNumber.ToUpper().Contains(term.Code)));
#pragma warning restore CA1304, CA1311, CA1862
        }

        // Montants comparés et triés en réel : SQLite ne compare pas les decimal (stockés en texte) ; à decimal(18,4),
        // la conversion ne change ni l'ordre ni les bornes saisies au centime.
        if (filter.MinAmount is decimal min)
        {
            double bound = (double)min;
            query = query.Where(p => (double)p.Amount >= bound);
        }

        if (filter.MaxAmount is decimal max)
        {
            double bound = (double)max;
            query = query.Where(p => (double)p.Amount <= bound);
        }

        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        List<Payment> page = await Sort(query, filter.Sort, filter.Direction)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return (page, total);
    }

    /// <summary>Tri en base : client sur sa forme sans accents, montant en réel ; le numéro, du plus récent au plus ancien, départage.</summary>
    private static IOrderedQueryable<Payment> Sort(IQueryable<Payment> payments, PaymentSortDto sort, SortDirectionDto direction)
    {
        IOrderedQueryable<Payment> ordered = sort switch
        {
            PaymentSortDto.Number => payments.By(p => p.Number, direction),
            PaymentSortDto.Customer => payments.By(p => EF.Property<string>(p, SearchKey.PropertyName), direction),
            PaymentSortDto.Amount => payments.By(p => (double)p.Amount, direction),
            _ => payments.By(p => p.Date, direction),
        };
        return ordered.ThenByDescending(p => p.Number);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> NumbersAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<Guid, string> numbers = [];
        foreach (PaymentId[] batch in ids.Distinct().Select(i => new PaymentId(i)).Chunk(BatchSize))
        {
            var found = await db.Set<Payment>().AsNoTracking().Where(p => batch.Contains(p.Id)).Select(p => new { p.Id, p.Number })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var payment in found)
            {
                numbers[payment.Id.Value] = payment.Number;
            }
        }

        return numbers;
    }
}

internal sealed class EfBankJournal(TenantDbSession session) : IBankJournal
{
    public async Task AddAsync(BankJournalEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        BankJournalRecord record = new()
        {
            Id = Guid.CreateVersion7(),
            PaymentId = entry.PaymentId.Value,
            IsReversal = entry.IsReversal,
            PieceNumber = entry.PieceNumber,
            Date = entry.Date,
            CustomerId = entry.CustomerId.Value,
            CustomerName = entry.CustomerName,
        };
        int number = 1;
        foreach (AccountingEntryLine line in entry.Lines)
        {
            record.Lines.Add(new BankJournalLineRecord
            {
                EntryId = record.Id,
                LineNumber = number++,
                Account = line.Account,
                Debit = line.Debit,
                Credit = line.Credit,
                VatRatePercent = line.VatRatePercent,
            });
        }

        await db.Set<BankJournalRecord>().AddAsync(record, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BankJournalEntry>> ForPaymentAsync(PaymentId payment, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<BankJournalRecord> records = await db.Set<BankJournalRecord>().AsNoTracking().Include(e => e.Lines)
            .Where(e => e.PaymentId == payment.Value).ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. records.OrderBy(r => r.IsReversal).Select(ToEntry)];
    }

    public async Task<IReadOnlyList<BankJournalEntry>> ReadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<BankJournalRecord> records = await db.Set<BankJournalRecord>().AsNoTracking().Include(e => e.Lines)
            .Where(e => e.Date >= from && e.Date <= to).ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. records.OrderBy(r => r.Date).ThenBy(r => r.Id).Select(ToEntry)];
    }

    private static BankJournalEntry ToEntry(BankJournalRecord record) => new(
        new PaymentId(record.PaymentId),
        record.IsReversal,
        record.PieceNumber,
        record.Date,
        new CustomerId(record.CustomerId),
        record.CustomerName,
        [.. record.Lines.OrderBy(l => l.LineNumber).Select(l => new AccountingEntryLine(l.Account, l.Debit, l.Credit, l.VatRatePercent))]);
}

internal sealed class EfBankStatementRepository(TenantDbSession session) : IBankStatementRepository
{
    private const int BatchSize = 1000;

    public async Task<IReadOnlySet<string>> ExistingFingerprintsAsync(IReadOnlyCollection<string> fingerprints, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        HashSet<string> existing = new(StringComparer.Ordinal);
        foreach (string[] batch in fingerprints.Distinct().Chunk(BatchSize))
        {
            List<string> found = await db.Set<BankStatementLine>().AsNoTracking().Where(l => batch.Contains(l.Fingerprint)).Select(l => l.Fingerprint)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            existing.UnionWith(found);
        }

        return existing;
    }

    public async Task AddImportAsync(BankStatementImport import, IReadOnlyList<BankStatementLine> lines, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<BankStatementImport>().AddAsync(import, cancellationToken).ConfigureAwait(false);
        await db.Set<BankStatementLine>().AddRangeAsync(lines, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BankStatementLine?> FindLineAsync(Guid id, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<BankStatementLine>().FirstOrDefaultAsync(l => l.Id == id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BankStatementLine>> ListLinesAsync(int max, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<BankStatementLine> pending = await db.Set<BankStatementLine>().AsNoTracking()
            .Where(l => l.Status == BankLineStatus.Pending)
            .OrderByDescending(l => l.BookingDate)
            .Take(max)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        int room = max - pending.Count;
        List<BankStatementLine> others = room <= 0
            ? []
            : await db.Set<BankStatementLine>().AsNoTracking()
                .Where(l => l.Status != BankLineStatus.Pending)
                .OrderByDescending(l => l.BookingDate)
                .Take(room)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. pending.OrderByDescending(l => l.BookingDate).ThenBy(l => l.Id), .. others.OrderByDescending(l => l.BookingDate).ThenBy(l => l.Id)];
    }

    public async Task<(int Pending, int Matched, int Ignored)> CountAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        var counts = await db.Set<BankStatementLine>().AsNoTracking().GroupBy(l => l.Status).Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        int Of(BankLineStatus status) => counts.FirstOrDefault(c => c.Status == status)?.Count ?? 0;
        return (Of(BankLineStatus.Pending), Of(BankLineStatus.Matched), Of(BankLineStatus.Ignored));
    }

    public async Task<IReadOnlyList<BankStatementImport>> ListImportsAsync(int max, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<BankStatementImport> imports = await db.Set<BankStatementImport>().AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. imports.OrderByDescending(i => i.ImportedAt).Take(max)];
    }

    public async Task<BankReconciliationStatus> StatusAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        var imports = await db.Set<BankStatementImport>().AsNoTracking().Select(i => new { i.ImportedAt, i.To }).ToListAsync(cancellationToken).ConfigureAwait(false);
        int pending = await db.Set<BankStatementLine>().CountAsync(l => l.Status == BankLineStatus.Pending, cancellationToken).ConfigureAwait(false);
        DateOnly? oldest = pending == 0
            ? null
            : await db.Set<BankStatementLine>().AsNoTracking().Where(l => l.Status == BankLineStatus.Pending).OrderBy(l => l.BookingDate)
                .Select(l => (DateOnly?)l.BookingDate).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        List<DateTimeOffset?> matched = await db.Set<BankStatementLine>().AsNoTracking().Where(l => l.Status == BankLineStatus.Matched)
            .Select(l => l.ChangedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new BankReconciliationStatus(
            imports.Count == 0 ? null : imports.Max(i => i.ImportedAt),
            imports.Max(i => i.To),
            pending,
            oldest,
            matched.Max());
    }
}

internal sealed class EfLetteringRepository(TenantDbSession session) : ILetteringRepository
{
    private const int BatchSize = 1000;

    public async Task AddAsync(Lettering lettering, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<Lettering>().AddAsync(lettering, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Lettering>> ListForCustomerAsync(CustomerId customer, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<Lettering>().Where(l => l.CustomerId == customer).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Lettering>> ListActiveContainingAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<Guid, Lettering> found = [];
        foreach (Guid[] batch in documentIds.Distinct().Chunk(BatchSize))
        {
            List<Lettering> letterings = await db.Set<Lettering>().AsNoTracking()
                .Where(l => l.CancelledAt == null && l.Items.Any(i => batch.Contains(i.DocumentId)))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (Lettering lettering in letterings)
            {
                found[lettering.Id] = lettering;
            }
        }

        return [.. found.Values];
    }

    public async Task<IReadOnlyList<Lettering>> ListActiveAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<Lettering>().AsNoTracking().Where(l => l.CancelledAt == null).ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class EfBankingSettingsRepository(TenantDbSession session) : IBankingSettingsRepository
{
    public async Task<BankingSettings?> FindAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<BankingSettings>().FirstOrDefaultAsync(s => s.Id == BankingSettings.SingletonId, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(BankingSettings settings, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<BankingSettings>().AddAsync(settings, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class EfKnownIbanStore(TenantDbSession session) : IKnownIbanStore
{
    public async Task<IReadOnlyList<KnownIban>> ListAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<KnownCustomerIbanRecord> records = await db.Set<KnownCustomerIbanRecord>().AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. records.Select(r => new KnownIban(new CustomerId(r.CustomerId), r.IbanHash))];
    }

    public async Task RememberAsync(CustomerId customer, string ibanHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        if (await db.Set<KnownCustomerIbanRecord>().FindAsync([customer.Value, ibanHash], cancellationToken).ConfigureAwait(false) is null)
        {
            await db.Set<KnownCustomerIbanRecord>().AddAsync(
                new KnownCustomerIbanRecord { CustomerId = customer.Value, IbanHash = ibanHash, LearnedAtMs = now.ToUnixTimeMilliseconds() },
                cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Les pièces de vente validées et leur TVA en attente. La TVA en attente d'une facture vient des lignes 44574 de son
/// écriture de vente et de celles de ses avoirs (au débit), moins les parts des encaissements non contre-passés.
/// </summary>
internal sealed class EfReceivableDocumentReader(TenantDbSession session) : IReceivableDocumentReader
{
    private const int BatchSize = 500;

    public async Task<IReadOnlyList<ReceivableDocument>> ListAsync(CustomerId? customer, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<Invoice> query = db.Set<Invoice>().AsNoTracking().Where(i => i.Status != InvoiceStatus.Draft && i.Number != null);
        if (customer is CustomerId c)
        {
            query = query.Where(i => i.CustomerId == c);
        }

        var rows = await query
            .Select(i => new
            {
                i.Id,
                i.Kind,
                i.Number,
                i.CustomerId,
                CustomerName = i.Customer.Name,
                i.IssueDate,
                i.DueDate,
                i.TotalInclTax,
                i.CreditedInclTax,
                i.PaidInclTax,
                i.CreditedInvoiceId,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return
        [
            .. rows.Select(r => new ReceivableDocument(
                r.Id, r.Kind, r.Number!, r.CustomerId, r.CustomerName, r.IssueDate ?? default, r.DueDate, r.TotalInclTax, r.CreditedInclTax, r.PaidInclTax, r.CreditedInvoiceId)),
        ];
    }

    public async Task<IReadOnlyDictionary<InvoiceId, IReadOnlyDictionary<decimal, decimal>>> PendingVatAsync(
        IReadOnlyCollection<InvoiceId> invoices, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<InvoiceId, Dictionary<decimal, decimal>> pending = [];
        void Add(InvoiceId invoice, decimal rate, decimal amount)
        {
            if (!pending.TryGetValue(invoice, out Dictionary<decimal, decimal>? byRate))
            {
                pending[invoice] = byRate = [];
            }

            byRate[rate] = byRate.GetValueOrDefault(rate) + amount;
        }

        foreach (InvoiceId[] batch in invoices.Distinct().Chunk(BatchSize))
        {
            // Les avoirs de ces factures : leur 44574 au débit réduit la TVA en attente de la facture d'origine. Liste de
            // nullables : PostgreSQL ne convertit pas un tableau d'InvoiceId comparé à une colonne nullable.
            InvoiceId?[] origins = [.. batch.Select(b => (InvoiceId?)b)];
            var credits = await db.Set<Invoice>().AsNoTracking()
                .Where(i => i.CreditedInvoiceId != null && origins.Contains(i.CreditedInvoiceId) && i.Status != InvoiceStatus.Draft)
                .Select(i => new { i.Id, Origin = i.CreditedInvoiceId!.Value })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            Dictionary<Guid, InvoiceId> owner = batch.ToDictionary(b => b.Value, b => b);
            foreach (var credit in credits)
            {
                owner[credit.Id.Value] = credit.Origin;
            }

            Guid[] documents = [.. owner.Keys];
            List<SalesJournalRecord> entries = await db.Set<SalesJournalRecord>().AsNoTracking().Include(e => e.Lines)
                .Where(e => documents.Contains(e.InvoiceId))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (SalesJournalRecord entry in entries)
            {
                foreach (SalesJournalLineRecord line in entry.Lines.Where(l => l.Account == SalesAccounts.VatPending))
                {
                    Add(owner[entry.InvoiceId], line.VatRatePercent ?? 0m, line.Credit - line.Debit);
                }
            }

            List<Payment> paid = await db.Set<Payment>().AsNoTracking()
                .Where(p => p.Status == PaymentStatus.Posted && p.VatShares.Any(s => batch.Contains(s.InvoiceId)))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            HashSet<InvoiceId> wanted = [.. batch];
            foreach (PaymentVatShare share in paid.SelectMany(p => p.VatShares).Where(s => wanted.Contains(s.InvoiceId)))
            {
                Add(share.InvoiceId, share.VatRatePercent, -share.Amount);
            }
        }

        return pending.ToDictionary(
            p => p.Key,
            p => (IReadOnlyDictionary<decimal, decimal>)p.Value.Where(r => r.Value > 0m).ToDictionary(r => r.Key, r => r.Value));
    }
}
