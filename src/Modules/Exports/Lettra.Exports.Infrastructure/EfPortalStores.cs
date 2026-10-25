using Lettra.Domain.Billing;
using Lettra.Domain.Portal;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Portal;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Exports.Infrastructure;

// Espace client dans la base du dossier : liens (empreinte du secret seulement) et preuves de signature des devis.

internal sealed class EfPortalLinkRepository(TenantDbSession session) : IPortalLinkRepository
{
    public async Task AddAsync(PortalLink link, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<PortalLink>().AddAsync(link, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PortalLink?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<PortalLink>().FirstOrDefaultAsync(l => l.Id == id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PortalLink?> FindBySecretHashAsync(string secretHash, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<PortalLink>().FirstOrDefaultAsync(l => l.SecretHash == secretHash, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PortalLink>> ListByCustomerAsync(CustomerId customer, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<PortalLink>().AsNoTracking().Where(l => l.CustomerId == customer).ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class EfQuoteSignatureRepository(TenantDbSession session) : IQuoteSignatureRepository
{
    private const int BatchSize = 500;

    public async Task AddAsync(QuoteSignature signature, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<QuoteSignature>().AddAsync(signature, cancellationToken).ConfigureAwait(false);
    }

    public async Task<QuoteSignature?> FindByQuoteAsync(QuoteId quote, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<QuoteSignature>().FirstOrDefaultAsync(s => s.QuoteId == quote, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<QuoteId, QuoteSignature>> FindByQuotesAsync(IReadOnlyCollection<QuoteId> quotes, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<QuoteId, QuoteSignature> found = [];
        foreach (QuoteId[] batch in quotes.Distinct().Chunk(BatchSize))
        {
            List<QuoteSignature> signed = await db.Set<QuoteSignature>().AsNoTracking().Where(s => batch.Contains(s.QuoteId))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (QuoteSignature signature in signed)
            {
                found[signature.QuoteId] = signature;
            }
        }

        return found;
    }
}
