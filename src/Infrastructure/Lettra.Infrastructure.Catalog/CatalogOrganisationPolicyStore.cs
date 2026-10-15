using Lettra.Application.Abstractions.Identity;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Catalog;

/// <summary>Politiques des cabinets dans la base Catalog (modes 2, 3 et 5).</summary>
internal sealed class CatalogOrganisationPolicyStore(IDbContextFactory<CatalogDbContext> factory, TimeProvider clock) : IOrganisationPolicyStore
{
    public bool SupportsSegregationOfDuties => true;

    public async Task<SegregationOfDutiesPolicy> GetSegregationOfDutiesAsync(OrganisationId organisation, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            CatalogOrganisationPolicy? row = await db.OrganisationPolicies.AsNoTracking()
                .SingleOrDefaultAsync(p => p.OrganisationId == organisation.Value, cancellationToken).ConfigureAwait(false);
            return row is null
                ? SegregationOfDutiesPolicy.Disabled
                : new SegregationOfDutiesPolicy(row.SegregationOfDutiesEnabled, row.CreditNoteApprovalThresholdInclTax);
        }
    }

    public async Task SaveSegregationOfDutiesAsync(
        OrganisationId organisation, SegregationOfDutiesPolicy policy, Guid changedBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            CatalogOrganisationPolicy? row = await db.OrganisationPolicies
                .SingleOrDefaultAsync(p => p.OrganisationId == organisation.Value, cancellationToken).ConfigureAwait(false);
            if (row is null)
            {
                row = new CatalogOrganisationPolicy { OrganisationId = organisation.Value };
                db.OrganisationPolicies.Add(row);
            }

            row.SegregationOfDutiesEnabled = policy.Enabled;
            row.CreditNoteApprovalThresholdInclTax = policy.CreditNoteApprovalThresholdInclTax;
            row.UpdatedAt = clock.GetUtcNow();
            row.UpdatedBy = changedBy;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
