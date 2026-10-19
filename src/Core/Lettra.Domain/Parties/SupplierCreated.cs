using Lettra.Domain.Common;

namespace Lettra.Domain.Parties;

/// <summary>Un fournisseur a été créé dans le dossier. Ne porte que l'identifiant.</summary>
public sealed record SupplierCreated(SupplierId SupplierId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Un fournisseur a été modifié. Ne porte que l'identifiant.</summary>
public sealed record SupplierUpdated(SupplierId SupplierId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record SupplierArchiveChanged(SupplierId SupplierId, bool IsArchived, DateTimeOffset OccurredAt) : IDomainEvent;
