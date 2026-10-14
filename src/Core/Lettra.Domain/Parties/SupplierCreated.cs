using Lettra.Domain.Common;

namespace Lettra.Domain.Parties;

/// <summary>Un fournisseur a été créé dans le dossier. Ne porte que l'identifiant.</summary>
public sealed record SupplierCreated(SupplierId SupplierId, DateTimeOffset OccurredAt) : IDomainEvent;
