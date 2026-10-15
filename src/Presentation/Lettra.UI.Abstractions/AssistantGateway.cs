using Lettra.Contracts.Assistant;
using Lettra.Contracts.Common;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Assistant IA du dossier ouvert (annexe B §4.2). Il passe toujours par l'orchestrateur du serveur, ou du poste en mode
/// hors ligne : l'écran ne parle jamais directement à un moteur.
/// </summary>
public interface IAssistantGateway
{
    /// <summary>Fonctions ouvertes à l'utilisateur ; aucune quand l'IA est désactivée, ce qui est le réglage par défaut.</summary>
    Task<OperationResult<AssistantStatusResponse>> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Réponse au fil de l'eau. Un refus avant la première étape (droits, réglage du cabinet, serveur injoignable) arrive
    /// comme un unique <see cref="AssistantUnavailableEvent"/> portant le code de la raison.
    /// </summary>
    IAsyncEnumerable<AssistantEvent> AskAsync(AskAssistantRequest request, CancellationToken cancellationToken);
}
