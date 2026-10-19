using Lettra.Contracts.Common;
using Lettra.Domain.Common;

namespace Lettra.Application.Features;

/// <summary>Règles communes des listes triables : une colonne et un sens connus, des bornes dans l'ordre.</summary>
internal static class ListSortRules
{
    public static IEnumerable<Error> Validate<TSort>(TSort sort, SortDirectionDto direction)
        where TSort : struct, Enum
    {
        if (!Enum.IsDefined(sort))
        {
            yield return Error.Validation(ErrorCodes.Validation.Invalid, "Sort");
        }

        if (!Enum.IsDefined(direction))
        {
            yield return Error.Validation(ErrorCodes.Validation.Invalid, "Direction");
        }
    }

    public static IEnumerable<Error> ValidateRange(decimal? min, decimal? max)
    {
        if (min is { } low && max is { } high && low > high)
        {
            yield return Error.Validation(ErrorCodes.Validation.Invalid, "MaxAmount");
        }
    }
}
