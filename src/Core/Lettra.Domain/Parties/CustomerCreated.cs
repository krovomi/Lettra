using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Parties;

/// <summary>Un client a été créé dans le dossier. Ne porte que l'identifiant : aucune donnée de dossier ne voyage dans les événements techniques.</summary>
public sealed record CustomerCreated(CustomerId CustomerId, DateTimeOffset OccurredAt) : IDomainEvent;
