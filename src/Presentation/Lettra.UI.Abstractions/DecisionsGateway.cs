using Lettra.Contracts.Common;
using Lettra.Contracts.Decisions;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Décisions à prendre du dossier ouvert. Lire, actualiser et répondre demandent <c>DecisionsTraiter</c> (et la permission
/// de la règle de chaque décision) ; régler le cadran, <c>DecisionsRegler</c>. Refus possibles : <c>Decision.*</c>,
/// <c>Concurrency.VersionMismatch</c>, et ceux de la commande qu'une décision acceptée envoie (<c>Email.*</c>, <c>Reminders.*</c>…).
/// </summary>
public interface IDecisionsGateway
{
    Task<OperationResult<DecisionsResponse>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Fait tourner les règles maintenant, puis relit la liste.</summary>
    Task<OperationResult<DecisionsResponse>> RefreshAsync(CancellationToken cancellationToken);

    /// <summary>Accepter, remettre à plus tard, ignorer ou remettre dans la liste, à partir de la version lue ; relit la liste.</summary>
    Task<OperationResult<DecisionsResponse>> AnswerAsync(Guid decisionId, AnswerDecisionRequest request, long? expectedVersion, CancellationToken cancellationToken);

    /// <summary>Règle le cadran d'autonomie (<paramref name="expectedVersion"/> vide s'il n'a jamais été réglé) ; relit la liste.</summary>
    Task<OperationResult<DecisionsResponse>> SetPolicyAsync(SetDecisionPolicyRequest request, long? expectedVersion, CancellationToken cancellationToken);
}
