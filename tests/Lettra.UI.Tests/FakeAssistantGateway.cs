using System.Runtime.CompilerServices;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests;

/// <summary>Assistant IA de test : statut, réponse et journal fixés par le test ; les demandes reçues sont gardées.</summary>
internal sealed class FakeAssistantGateway : IAssistantGateway
{
    public AssistantStatusResponse Status { get; set; } = new(
        [AssistantFeature.DraftFromText, AssistantFeature.ExplainCompliance, AssistantFeature.RegulatoryQuestions], "local:mistral-small");

    /// <summary>Statut refusé (pas de droit « Utiliser l'assistant IA ») au lieu de <see cref="Status"/>.</summary>
    public string? StatusRefusal { get; set; }

    public IReadOnlyList<AssistantEvent> Answer { get; set; } = [];

    public IReadOnlyList<AssistantJournalEntry> Journal { get; set; } = [];

    public List<AskAssistantRequest> Requests { get; } = [];

    public int JournalReads { get; private set; }

    public int StatusReads { get; private set; }

    public Task<OperationResult<AssistantStatusResponse>> GetStatusAsync(CancellationToken cancellationToken)
    {
        StatusReads++;
        return Task.FromResult(StatusRefusal is { } code
            ? OperationResult<AssistantStatusResponse>.Failure([new ErrorDetail(code, code)])
            : OperationResult<AssistantStatusResponse>.Success(Status));
    }

    public async IAsyncEnumerable<AssistantEvent> AskAsync(AskAssistantRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Requests.Add(request);
        foreach (AssistantEvent item in Answer)
        {
            await Task.Yield();
            yield return item;
        }
    }

    public Task<OperationResult<IReadOnlyList<AssistantJournalEntry>>> GetJournalAsync(int limit, CancellationToken cancellationToken)
    {
        JournalReads++;
        return Task.FromResult(OperationResult<IReadOnlyList<AssistantJournalEntry>>.Success([.. Journal.Take(limit)]));
    }
}
