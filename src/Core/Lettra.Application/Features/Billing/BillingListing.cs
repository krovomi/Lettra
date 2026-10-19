using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Domain.Common;

namespace Lettra.Application.Features.Billing;

/// <summary>Règles communes du tri et des bornes de montant des listes de devis et de factures.</summary>
internal static class BillingListing
{
    public static IEnumerable<Error> Validate(BillingSortDto sort, SortDirectionDto direction, decimal? minAmount, decimal? maxAmount)
    {
        if (!Enum.IsDefined(sort))
        {
            yield return Error.Validation(ErrorCodes.Validation.Invalid, "Sort");
        }

        if (!Enum.IsDefined(direction))
        {
            yield return Error.Validation(ErrorCodes.Validation.Invalid, "Direction");
        }

        if (minAmount is { } min && maxAmount is { } max && min > max)
        {
            yield return Error.Validation(ErrorCodes.Validation.Invalid, "MaxAmount");
        }
    }
}
