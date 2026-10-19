using Lettra.Contracts.Common;
using Lettra.Domain.Common;

namespace Lettra.Application.Features;

/// <summary>Règles communes des listes paginées : page à partir de 1, taille bornée.</summary>
public static class Paging
{
    /// <summary>Taille de page maximale des listes, sauf exception déclarée par la requête (catalogue).</summary>
    public const int MaxPageSize = 200;

    public static IReadOnlyList<Error> Validate(int page, int pageSize, int maxPageSize = MaxPageSize)
    {
        List<Error> errors = [];
        if (page < 1)
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.Invalid, "Page"));
        }

        if (pageSize < 1 || pageSize > maxPageSize)
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.Invalid, "PageSize"));
        }

        return errors;
    }
}
