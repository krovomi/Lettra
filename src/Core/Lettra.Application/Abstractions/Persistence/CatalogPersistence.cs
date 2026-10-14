using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Domain.Catalog;

namespace Lettra.Application.Abstractions.Persistence;

/// <summary>Écriture des articles du catalogue.</summary>
public interface ICatalogItemRepository
{
    Task AddAsync(CatalogItem item, CancellationToken cancellationToken);

    Task<CatalogItem?> FindAsync(CatalogItemId id, CancellationToken cancellationToken);

    /// <summary>Vrai si un autre article du dossier porte déjà cette référence (clé en majuscules).</summary>
    Task<bool> ReferenceExistsAsync(string referenceKey, CatalogItemId? except, CancellationToken cancellationToken);
}

/// <summary>Critères de la liste des articles.</summary>
/// <param name="Search">Texte cherché dans la référence et la désignation (sans tenir compte de la casse).</param>
/// <param name="IncludeArchived">Inclure les articles archivés (écran Catalogue) ; jamais pour la saisie F4.</param>
/// <param name="Page">Page, à partir de 1.</param>
/// <param name="PageSize">Taille de page.</param>
public sealed record CatalogItemFilter(string? Search, bool IncludeArchived, int Page, int PageSize);

/// <summary>Lecture des articles : projection directe en contrats.</summary>
public interface ICatalogItemReadStore
{
    Task<PagedResponse<CatalogItemSummary>> ListAsync(CatalogItemFilter filter, CancellationToken cancellationToken);

    Task<CatalogItemResponse?> GetAsync(CatalogItemId id, CancellationToken cancellationToken);
}
