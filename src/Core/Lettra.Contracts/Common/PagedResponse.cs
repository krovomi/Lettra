namespace Lettra.Contracts.Common;

/// <summary>Page de résultats.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
