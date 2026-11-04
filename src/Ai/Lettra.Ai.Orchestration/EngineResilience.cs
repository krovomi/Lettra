using System.Collections.Concurrent;
using System.Text.Json;
using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Diagnostics;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;

namespace Lettra.Ai.Orchestration;

/// <summary>
/// Réglages du disjoncteur des moteurs d'IA (<c>Lettra:Ai:Resilience</c>). Le délai d'un appel est celui du moteur
/// (<c>Lettra:Ai:Claude:Timeout</c>, <c>Lettra:Ai:Local:Timeout</c>).
/// </summary>
public sealed class AiResilienceOptions
{
    public const string SectionName = "Lettra:Ai:Resilience";

    /// <summary>
    /// Appels au moteur, sur les cinq dernières minutes, à partir desquels le disjoncteur peut s'ouvrir (au moins la
    /// moitié en échec). Au moins 2.
    /// </summary>
    public int MinimumThroughput { get; set; } = 3;

    /// <summary>Durée pendant laquelle Lettra n'appelle plus un moteur en panne ; l'appel suivant sert d'essai. Au moins une seconde.</summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Un disjoncteur par type de moteur (Polly, la bibliothèque de <c>Microsoft.Extensions.Http.Resilience</c> déjà utilisée
/// pour l'annuaire et VIES). Il est posé sur le port <see cref="IAiEngine"/> plutôt que sur un client HTTP : il compte
/// ainsi aussi les réponses illisibles, et vaut pour tout moteur, qu'il passe par HTTP ou non. Après des échecs répétés,
/// Lettra cesse d'appeler le moteur quelques minutes et l'assistant se termine aussitôt sur « moteur en pause ».
/// </summary>
internal sealed class AiEngineCircuits(IOptions<AiResilienceOptions> options, TimeProvider time) : IAiEngineHealth
{
    private readonly ConcurrentDictionary<AiEngineKind, EngineCircuit> _circuits = new();

    public bool IsResponding(AiEngineKind kind) => !_circuits.TryGetValue(kind, out EngineCircuit? circuit) || circuit.IsResponding;

    /// <summary>Le moteur, derrière le disjoncteur de son type.</summary>
    public IAiEngine Guard(AiEngineKind kind, IAiEngine engine) =>
        new GuardedAiEngine(engine, _circuits.GetOrAdd(kind, k => new EngineCircuit(k, options.Value, time)));

    /// <summary>Nom technique d'un type de moteur, pour les métriques (<c>claude_eu</c>, <c>local</c>).</summary>
    public static string MetricName(AiEngineKind kind) => kind switch
    {
        AiEngineKind.ClaudeEu => "claude_eu",
        AiEngineKind.Local => "local",
        _ => "none",
    };

    private sealed class GuardedAiEngine(IAiEngine inner, EngineCircuit circuit) : IAiEngine
    {
        public string Name => inner.Name;

        public Task<EngineTurn> CompleteAsync(EngineRequest request, CancellationToken cancellationToken) =>
            circuit.ExecuteAsync(inner, request, cancellationToken);
    }
}

/// <summary>Le disjoncteur d'un type de moteur, et l'état qu'il publie (« IA disponible »).</summary>
internal sealed class EngineCircuit
{
    private static readonly TimeSpan SamplingDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ShortestBreak = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _time;
    private readonly string _metricName;
    private readonly ResiliencePipeline _pipeline;
    private long _pausedUntilTicks;

    public EngineCircuit(AiEngineKind kind, AiResilienceOptions options, TimeProvider time)
    {
        _time = time;
        _metricName = AiEngineCircuits.MetricName(kind);
        _pipeline = new ResiliencePipelineBuilder { TimeProvider = time }
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = Math.Max(2, options.MinimumThroughput),
                SamplingDuration = SamplingDuration,
                BreakDuration = options.BreakDuration < ShortestBreak ? ShortestBreak : options.BreakDuration,

                // Toute erreur du moteur compte, délai compris ; l'arrêt demandé par l'utilisateur ne compte pas.
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Exception is { } e && !(e is OperationCanceledException && args.Context.CancellationToken.IsCancellationRequested)),
                OnOpened = args =>
                {
                    Interlocked.Exchange(ref _pausedUntilTicks, (_time.GetUtcNow() + args.BreakDuration).UtcTicks);
                    LettraTelemetry.Ai.ReportEngine(_metricName, available: false);
                    return default;
                },
                OnClosed = _ =>
                {
                    Interlocked.Exchange(ref _pausedUntilTicks, 0);
                    LettraTelemetry.Ai.ReportEngine(_metricName, available: true);
                    return default;
                },
            })
            .Build();
        LettraTelemetry.Ai.ReportEngine(_metricName, available: true);
    }

    /// <summary>Faux pendant la pause qui suit des échecs répétés ; ensuite, l'appel suivant sert d'essai.</summary>
    public bool IsResponding => _time.GetUtcNow().UtcTicks >= Interlocked.Read(ref _pausedUntilTicks);

    public Task<EngineTurn> ExecuteAsync(IAiEngine engine, EngineRequest request, CancellationToken cancellationToken) =>
        _pipeline.ExecuteAsync(
            static (state, ct) => new ValueTask<EngineTurn>(state.Engine.CompleteAsync(state.Request, ct)),
            (Engine: engine, Request: request),
            cancellationToken).AsTask();
}

/// <summary>Raisons d'un repli sans IA : classement d'une erreur du moteur, texte du journal et nom de la métrique.</summary>
internal static class EngineFailures
{
    /// <summary>
    /// La raison d'une erreur du moteur, cherchée de l'erreur la plus extérieure à la plus intérieure : celle que l'adapter
    /// a nommée, le disjoncteur ouvert, un délai dépassé, un JSON illisible ; sinon le moteur est réputé injoignable.
    /// </summary>
    public static AiEngineFailure Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case AiEngineException engine:
                    return engine.Failure;
                case BrokenCircuitException:
                    return AiEngineFailure.Paused;
                case TimeoutException or OperationCanceledException:
                    return AiEngineFailure.Timeout;
                case JsonException:
                    return AiEngineFailure.Unreadable;
            }
        }

        return AiEngineFailure.Unreachable;
    }

    /// <summary>Texte du journal IA du dossier : la raison, en clair, sans détail technique qui pourrait porter une donnée.</summary>
    public static string Describe(AiEngineFailure failure) => failure switch
    {
        AiEngineFailure.Timeout => "délai dépassé",
        AiEngineFailure.Unreadable => "réponse illisible",
        AiEngineFailure.Paused => "moteur en pause après des échecs répétés",
        _ => "moteur injoignable",
    };

    /// <summary>Valeur de l'étiquette <c>lettra.ai.fallback.reason</c>.</summary>
    public static string MetricName(AiEngineFailure failure) => failure switch
    {
        AiEngineFailure.Timeout => "timeout",
        AiEngineFailure.Unreadable => "unreadable",
        AiEngineFailure.Paused => "paused",
        _ => "unreachable",
    };
}
