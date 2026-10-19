using System.Linq.Expressions;
using Lettra.Contracts.Common;

namespace Lettra.Infrastructure.Persistence.Repositories;

/// <summary>Tri d'une liste dans le sens demandé ; l'appelant ajoute ses clés de départage (<c>ThenBy</c>).</summary>
internal static class ListOrdering
{
    public static IOrderedQueryable<T> By<T, TKey>(this IQueryable<T> query, Expression<Func<T, TKey>> key, SortDirectionDto direction) =>
        direction == SortDirectionDto.Descending ? query.OrderByDescending(key) : query.OrderBy(key);
}
