using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Domain.Billing;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Concurrency;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Billing;

/// <summary>
/// Modèles récurrents (vague 7) : lignes dans <c>RecurringInvoiceLines</c> (mêmes colonnes que les lignes des pièces),
/// factures créées dans <c>RecurringInvoiceOccurrences</c> avec la clé (modèle, début de période) qui interdit un doublon.
/// </summary>
internal sealed class RecurringInvoiceConfiguration : IEntityTypeConfiguration<RecurringInvoice>
{
    public void Configure(EntityTypeBuilder<RecurringInvoice> builder)
    {
        builder.ToTable("RecurringInvoices");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasConversion(id => id.Value, value => new RecurringInvoiceId(value)).ValueGeneratedNever();
        builder.Property(r => r.Name).HasMaxLength(RecurringInvoice.MaxNameLength).IsRequired();
        builder.Property(r => r.CustomerId).HasConversion(id => id.Value, value => new CustomerId(value)).IsRequired();
        builder.Property(r => r.VatRegime).HasConversion<int>();
        builder.Property(r => r.Subject).HasMaxLength(BillingDocument<InvoiceId>.MaxSubjectLength);
        builder.Property(r => r.Notes).HasMaxLength(BillingDocument<InvoiceId>.MaxNotesLength);
        builder.Property(r => r.PaymentTerms).HasMaxLength(RecurringInvoice.MaxPaymentTermsLength);
        builder.Property(r => r.Frequency).HasConversion<int>();
        builder.Property(r => r.Timing).HasConversion<int>();
        builder.Property(r => r.Revision).HasConversion<int>();
        builder.IsVersioned();
        builder.Ignore(r => r.NextPeriod);
        builder.Ignore(r => r.RevisionAwaitsIndex);
        builder.Ignore(r => r.DomainEvents);

        builder.OwnsMany(r => r.Lines, line =>
        {
            line.ToTable("RecurringInvoiceLines");
            line.WithOwner().HasForeignKey("RecurringInvoiceId");
            line.HasKey("RecurringInvoiceId", nameof(DocumentLine.Position));
            line.Property(l => l.Position).ValueGeneratedNever();
            line.Property(l => l.Reference).HasMaxLength(DocumentLine.MaxReferenceLength);
            line.Property(l => l.Designation).HasMaxLength(DocumentLine.MaxDesignationLength).IsRequired();
            line.Property(l => l.Description).HasMaxLength(DocumentLine.MaxDescriptionLength);
            line.Property(l => l.Nature).HasConversion<int>();
            line.Property(l => l.UnitCode).HasMaxLength(3).IsRequired();
            line.Property(l => l.VatRateCode).HasMaxLength(10).IsRequired();
            line.Property(l => l.VatCategory).HasMaxLength(2).IsRequired();
            line.Property(l => l.VatExemptionCode).HasMaxLength(30);
            line.Ignore(l => l.Kind);
        });
        builder.Navigation(r => r.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(r => r.Occurrences, occurrence =>
        {
            occurrence.ToTable("RecurringInvoiceOccurrences");
            occurrence.WithOwner().HasForeignKey("RecurringInvoiceId");
            occurrence.HasKey("RecurringInvoiceId", nameof(RecurringOccurrence.PeriodStart));
            occurrence.Property(o => o.InvoiceId).HasConversion(id => id.Value, value => new InvoiceId(value)).IsRequired();
            occurrence.HasIndex(o => o.InvoiceId).IsUnique();
        });
        builder.Navigation(r => r.Occurrences).HasField("_occurrences").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(r => r.CustomerId);
        builder.HasIndex(r => new { r.IsActive, r.NextIssueDate });
    }
}

internal sealed class RecurringInvoiceRepository(TenantDbSession session) : IRecurringInvoiceRepository
{
    public async Task AddAsync(RecurringInvoice template, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<RecurringInvoice>().AddAsync(template, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RecurringInvoice?> FindAsync(RecurringInvoiceId id, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<RecurringInvoice>().AsTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RecurringInvoiceId>> ListDueAsync(DateOnly today, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<RecurringInvoice>().AsNoTracking()
            .Where(r => r.IsActive && r.NextIssueDate != null && r.NextIssueDate <= today)
            .OrderBy(r => r.NextIssueDate)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RemoveAsync(RecurringInvoice template, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        db.Set<RecurringInvoice>().Remove(template);
    }
}

internal sealed class RecurringInvoiceReadStore(TenantDbSession session) : IRecurringInvoiceReadStore
{
    public async Task<PagedResponse<RecurringInvoiceSummary>> ListAsync(RecurringInvoiceListFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        var query =
            from r in db.Set<RecurringInvoice>().AsNoTracking()
            join c in db.Set<Customer>().AsNoTracking() on r.CustomerId equals c.Id into customers
            from c in customers.DefaultIfEmpty()
            select new { Template = r, CustomerName = c == null ? string.Empty : c.Name };

        if (filter.Customer is { } customer)
        {
            query = query.Where(x => x.Template.CustomerId == customer);
        }

        if (filter.Active is { } active)
        {
            query = query.Where(x => x.Template.IsActive == active);
        }

        if (filter.DueOnly)
        {
            query = query.Where(x => x.Template.IsActive && x.Template.NextIssueDate != null && x.Template.NextIssueDate <= filter.Today);
        }

        if (filter.Search is { Length: > 0 } search)
        {
            string term = search.ToUpperInvariant();
#pragma warning disable CA1304, CA1311, CA1862 // Arbre d'expression traduit en UPPER(...) SQL : jamais exécuté en .NET.
            query = query.Where(x => x.Template.Name.ToUpper().Contains(term)
                || (x.Template.Subject ?? string.Empty).ToUpper().Contains(term)
                || (x.CustomerName ?? string.Empty).ToUpper().Contains(term));
#pragma warning restore CA1304, CA1311, CA1862
        }

        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        query = (filter.Sort, filter.Descending) switch
        {
            (RecurringInvoiceSortDto.Name, false) => query.OrderBy(x => x.Template.Name),
            (RecurringInvoiceSortDto.Name, true) => query.OrderByDescending(x => x.Template.Name),
            (RecurringInvoiceSortDto.Customer, false) => query.OrderBy(x => x.CustomerName).ThenBy(x => x.Template.Name),
            (RecurringInvoiceSortDto.Customer, true) => query.OrderByDescending(x => x.CustomerName).ThenBy(x => x.Template.Name),
            (RecurringInvoiceSortDto.Amount, false) => query.OrderBy(x => (double)x.Template.TotalExclTax),
            (RecurringInvoiceSortDto.Amount, true) => query.OrderByDescending(x => (double)x.Template.TotalExclTax),
            // Prochaine facture d'abord ; les modèles sans prochaine facture (terminés, suspendus) en dernier.
            (_, false) => query.OrderBy(x => x.Template.NextIssueDate == null).ThenBy(x => x.Template.NextIssueDate).ThenBy(x => x.Template.Name),
            (_, true) => query.OrderBy(x => x.Template.NextIssueDate == null).ThenByDescending(x => x.Template.NextIssueDate).ThenBy(x => x.Template.Name),
        };

        var rows = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(x => new
            {
                x.Template.Id,
                x.Template.Name,
                x.Template.CustomerId,
                x.CustomerName,
                x.Template.Frequency,
                x.Template.TotalExclTax,
                x.Template.IsActive,
                x.Template.ValidateAutomatically,
                x.Template.NextIssueDate,
                x.Template.Revision,
                x.Template.SyntecReferenceIndex,
                x.Template.SyntecLatestIndex,
                x.Template.NextRevisionDate,
                x.Template.GeneratedCount,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResponse<RecurringInvoiceSummary>(
            [.. rows.Select(r => new RecurringInvoiceSummary(
                r.Id.Value,
                r.Name,
                r.CustomerId.Value,
                r.CustomerName,
                (RecurrenceFrequencyDto)(int)r.Frequency,
                r.TotalExclTax,
                r.IsActive,
                r.ValidateAutomatically,
                r.NextIssueDate,
                r.IsActive && r.NextIssueDate is { } next && next <= filter.Today,
                // Approximation de RecurringInvoice.RevisionAwaitsIndex pour la liste : anniversaire atteint, indice inchangé.
                r.Revision == PriceRevisionKind.SyntecIndex && r.NextIssueDate is { } issue && r.NextRevisionDate <= issue
                    && r.SyntecLatestIndex == r.SyntecReferenceIndex,
                r.GeneratedCount))],
            filter.Page,
            filter.PageSize,
            total);
    }

    public async Task<IReadOnlyDictionary<InvoiceId, RecurringInvoiceState>> InvoicesAsync(IReadOnlyCollection<InvoiceId> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return new Dictionary<InvoiceId, RecurringInvoiceState>();
        }

        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<InvoiceId> wanted = [.. ids];
        return await db.Set<Invoice>().AsNoTracking()
            .Where(i => wanted.Contains(i.Id))
            .Select(i => new RecurringInvoiceState(i.Id, i.Number, i.Status))
            .ToDictionaryAsync(s => s.Id, cancellationToken)
            .ConfigureAwait(false);
    }
}
