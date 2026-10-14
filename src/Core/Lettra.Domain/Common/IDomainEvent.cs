namespace Lettra.Domain.Common;

/// <summary>Fait métier survenu dans un agrégat, publié après persistance (outbox).</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
