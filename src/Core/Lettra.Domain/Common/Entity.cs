namespace Lettra.Domain.Common;

/// <summary>Entité : identité stable, égalité par identifiant.</summary>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : struct, IEquatable<TId>
{
    protected Entity(TId id) => Id = id;

    public TId Id { get; private init; }

    public bool Equals(Entity<TId>? other) =>
        other is not null && other.GetType() == GetType() && other.Id.Equals(Id);

    public override bool Equals(object? obj) => Equals(obj as Entity<TId>);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);
}

/// <summary>
/// Racine d'agrégat : frontière de cohérence et de transaction. Les agrégats
/// <b>enregistrent</b> leurs événements ; l'infrastructure les récupère après persistance.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : struct, IEquatable<TId>
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id)
        : base(id)
    {
    }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
