using Lettra.UI.Gateway.Http.Device;

namespace Lettra.Api.Tests;

/// <summary>
/// Sonde du serveur commun dans les tests : son attente maximale ne s'écoule que quand le test le décide. Sous la
/// charge de la CI (couverture, conteneurs, classes de tests en parallèle), un serveur de test neuf met parfois plus que
/// ce budget à répondre la première fois ; le budget lui-même se vérifie à part, avec <see cref="ProbeClock.Expired"/>.
/// </summary>
internal static class SharedServerProbes
{
    public static SharedServerProbe Probe(HttpMessageHandler server, TimeProvider? clock = null) =>
        new(new HttpClient(server), clock ?? ProbeClock.Frozen);
}

/// <summary>Horloge de la sonde : ses minuteries ne se déclenchent jamais (<see cref="Frozen"/>) ou tout de suite (<see cref="Expired"/>).</summary>
internal sealed class ProbeClock : TimeProvider
{
    private readonly bool _expired;

    private ProbeClock(bool expired) => _expired = expired;

    /// <summary>Le budget ne s'écoule pas : le test vérifie la réponse du serveur, quelle que soit la charge.</summary>
    public static ProbeClock Frozen { get; } = new(expired: false);

    /// <summary>Le budget est déjà écoulé : le serveur qui n'a pas encore répondu est jugé injoignable.</summary>
    public static ProbeClock Expired { get; } = new(expired: true);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (_expired)
        {
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }

        return new IdleTimer();
    }

    private sealed class IdleTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
