namespace Lettra.Exports.Domain.Platform;

/// <summary>Un statut du cycle de vie, tel que la plateforme le restitue.</summary>
public sealed record PlatformStatusEvent(PlatformStatus Status, DateTimeOffset At, string? Reason = null, decimal? Amount = null);
