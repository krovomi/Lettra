using Lettra.Domain.Common;

namespace Lettra.Domain.Catalog;

/// <summary>Un article a été ajouté au catalogue.</summary>
public sealed record CatalogItemCreated(CatalogItemId CatalogItemId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Un article a été modifié (les documents déjà émis gardent leur copie de la ligne).</summary>
public sealed record CatalogItemUpdated(CatalogItemId CatalogItemId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Un article a été archivé (ou réactivé) : il n'est plus proposé à la saisie.</summary>
public sealed record CatalogItemArchiveChanged(CatalogItemId CatalogItemId, bool IsArchived, DateTimeOffset OccurredAt) : IDomainEvent;
