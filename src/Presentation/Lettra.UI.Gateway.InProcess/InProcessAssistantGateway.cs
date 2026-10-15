using System.Runtime.CompilerServices;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Assistant;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Common;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>
/// Même cas d'usage que la route <c>/assistant</c> de l'API, donc le même orchestrateur, sur le poste (mode 1) : avec un
/// modèle local (Ollama, vLLM), rien ne quitte la machine. Le scope reste ouvert le temps de la réponse.
/// </summary>
internal sealed class InProcessAssistantGateway(IServiceScopeFactory scopes) : IAssistantGateway
{
    public Task<OperationResult<AssistantStatusResponse>> GetStatusAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetAssistantStatusQuery(dossier), ct), cancellationToken);

    public async IAsyncEnumerable<AssistantEvent> AskAsync(AskAssistantRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        AsyncServiceScope scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            Result initialized = await scope.ServiceProvider.GetRequiredService<ITenantInitializer>()
                .InitializeAsync(cancellationToken).ConfigureAwait(false);
            if (initialized.IsFailure)
            {
                yield return new AssistantUnavailableEvent(initialized.Errors[0].Code);
                yield break;
            }

            DossierId dossier = scope.ServiceProvider.GetRequiredService<ITenantContext>().Current.DossierId;
            Result<IAsyncEnumerable<AssistantEvent>> answer = await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>()
                .QueryAsync(new AskAssistantQuery(dossier, request.Feature, request.Question, request.OpenDocumentId), cancellationToken)
                .ConfigureAwait(false);
            if (answer.IsFailure)
            {
                yield return new AssistantUnavailableEvent(answer.Errors[0].Code);
                yield break;
            }

            await foreach (AssistantEvent assistantEvent in answer.Value.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return assistantEvent;
            }
        }
    }
}
