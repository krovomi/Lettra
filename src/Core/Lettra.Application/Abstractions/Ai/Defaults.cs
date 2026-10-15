using System.Runtime.CompilerServices;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Common;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Ai;

/// <summary>
/// Implémentations par défaut quand la couche IA n'est pas composée (tests, hôte sans IA) : l'assistant est désactivé,
/// comme le veut le réglage par défaut (annexe F §10). La composition de la couche IA les remplace.
/// </summary>
internal sealed class DisabledAiUsagePolicy : IAiUsagePolicy
{
    public ValueTask<AiSettings> GetSettingsAsync(OrganisationId organisation, CancellationToken cancellationToken) =>
        ValueTask.FromResult(AiSettings.Disabled);
}

internal sealed class NoAiEngineFactory : IAiEngineFactory
{
    public IAiEngine? Resolve(AiEngineKind kind) => null;
}

internal sealed class NoAiSettingsStore : IAiSettingsStore
{
    public bool IsAvailable => false;

    public ValueTask<StoredAiSettings?> FindAsync(OrganisationId organisation, CancellationToken cancellationToken) =>
        ValueTask.FromResult<StoredAiSettings?>(null);

    public ValueTask<Result<StoredAiSettings>> SaveAsync(
        OrganisationId organisation,
        AiSettings settings,
        long? expectedVersion,
        Guid updatedBy,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<Result<StoredAiSettings>>(new Error(ErrorCodes.Ai.SettingsUnavailable, ErrorKind.Validation));
}

internal sealed class EmptyAiAuditLog : IAiAuditLog
{
    public ValueTask AppendAsync(AiAuditEntry entry, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask<IReadOnlyList<AiAuditEntry>> ReadAsync(DossierId dossier, int limit, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<AiAuditEntry>>([]);
}

internal sealed class UnavailableAccountingAssistant : IAccountingAssistant
{
    public async IAsyncEnumerable<AssistantEvent> AskAsync(AssistantRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return new AssistantUnavailableEvent(ErrorCodes.Ai.EngineUnavailable);
    }
}
