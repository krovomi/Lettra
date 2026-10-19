using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Domain.Common;

namespace Lettra.Application.Features.Billing;

/// <summary>Règles communes du tri et des bornes de montant des listes de devis et de factures.</summary>
internal static class BillingListing
{
    public static IEnumerable<Error> Validate(BillingSortDto sort, SortDirectionDto direction, decimal? minAmount, decimal? maxAmount) =>
        ListSortRules.Validate(sort, direction).Concat(ListSortRules.ValidateRange(minAmount, maxAmount));
}
