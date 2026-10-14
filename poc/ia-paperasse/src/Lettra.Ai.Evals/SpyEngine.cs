using Lettra.Ai.Abstractions;

namespace Lettra.Ai.Evals;

/// <summary>Enveloppe d'un moteur qui garde une copie de tout ce qui lui est envoyé, pour vérifier qu'aucune donnée ne part en clair.</summary>
public sealed class SpyEngine(IAiEngine inner) : IAiEngine
{
    private readonly List<EngineRequest> _requests = [];

    public string Name => inner.Name;
    public IReadOnlyList<EngineRequest> Requests => _requests;

    public Task<EngineTurn> CompleteAsync(EngineRequest request, CancellationToken ct = default)
    {
        _requests.Add(request);
        return inner.CompleteAsync(request, ct);
    }

    /// <summary>Tout le texte reçu par le moteur, sauf la partie stable (surcouche et skill, sans donnée de dossier).</summary>
    public string EverythingSentExceptStableInstructions() => string.Join("\n", _requests.SelectMany(r =>
        new[] { r.Context }.Concat(r.Messages.SelectMany(m => m.Parts).Select(p => p switch
        {
            TextPart t => t.Text,
            ToolCallPart c => c.Input.GetRawText(),
            ToolResultPart res => res.Content,
            _ => "",
        }))));
}
