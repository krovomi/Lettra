using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Parties;

/// <summary>Un client a été créé dans le dossier. Ne porte que l'identifiant : aucune donnée de dossier ne voyage dans les événements techniques.</summary>
public sealed record CustomerCreated(CustomerId CustomerId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Un client a été modifié. Ne porte que l'identifiant.</summary>
public sealed record CustomerUpdated(CustomerId CustomerId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record CustomerArchiveChanged(CustomerId CustomerId, bool IsArchived, DateTimeOffset OccurredAt) : IDomainEvent;
