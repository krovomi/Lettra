using Lettra.Application.Abstractions.Identity;
using Lettra.Domain.Common;
using Lettra.Domain.Decisions;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Decisions;

/// <summary>
/// Une règle de « Décisions à prendre ». Comme une source d'indicateur de la vue cabinet, elle tourne <b>dans la portée
/// d'un dossier</b> (sa base est le dossier courant), à la passe quotidienne et quand quelqu'un demande « Actualiser ».
/// Elle ne fait que lire et proposer, sans utilisateur courant : elle lit ses services et ses stores, jamais le médiateur.
/// Chaque module enregistre la sienne (<c>services.AddScoped&lt;IDecisionRule, …&gt;()</c>) ; une règle en échec
/// n'empêche pas les autres, et ses décisions ouvertes restent telles quelles.
/// </summary>
public interface IDecisionRule
{
    /// <summary>Clé stable du type (« relance.due ») : nom des phrases <c>Decision.&lt;Kind&gt;.Title</c> et <c>.Reason</c>.</summary>
    string Kind { get; }

    /// <summary>Monte quand la règle change ; gardée avec chaque décision.</summary>
    int Version { get; }

    /// <summary>
    /// Qui voit et traite ses décisions : la permission qu'il faut pour agir sur le sujet (vérifier un IBAN demande de
    /// modifier les tiers). Une décision n'est jamais montrée à qui ne pourrait rien en faire.
    /// </summary>
    Permission Permission { get; }

    Task<IReadOnlyList<DecisionProposal>> ProposeAsync(DateOnly today, CancellationToken cancellationToken);
}

/// <summary>
/// Fait l'action d'une décision acceptée : envoie une commande existante par le médiateur, avec les droits de
/// l'utilisateur qui accepte (le pipeline vérifie la permission de la commande). Jamais de chemin de côté.
/// </summary>
public interface IDecisionActionHandler
{
    string ActionKind { get; }

    /// <summary>
    /// L'action se défait-elle sans conséquence hors de Lettra ? Un e-mail envoyé ne se reprend pas. Seules des actions
    /// réversibles, sur une liste fermée vérifiée par un test d'architecture, pourront un jour se faire sans clic.
    /// </summary>
    bool Reversible { get; }

    Task<Result> ExecuteAsync(DossierId dossier, string payload, CancellationToken cancellationToken);
}

/// <summary>Les décisions et le cadran d'autonomie, dans la base du dossier.</summary>
public interface IDecisionStore
{
    /// <summary>Toutes les décisions gardées (ouvertes et closes), modifiables : l'actualisation les rapproche des propositions.</summary>
    Task<IReadOnlyList<Decision>> ListForRefreshAsync(CancellationToken cancellationToken);

    /// <summary>Les décisions ouvertes, et celles closes depuis <paramref name="closedSince"/>, en lecture.</summary>
    Task<IReadOnlyList<Decision>> ListAsync(DateTimeOffset closedSince, CancellationToken cancellationToken);

    /// <summary>Une décision modifiable, ou <c>null</c>.</summary>
    Task<Decision?> FindAsync(DecisionId id, CancellationToken cancellationToken);

    Task AddAsync(Decision decision, CancellationToken cancellationToken);

    Task RemoveAsync(Decision decision, CancellationToken cancellationToken);

    /// <summary>Le cadran du dossier (modifiable), ou <c>null</c> tant qu'il n'a jamais été réglé ni actualisé.</summary>
    Task<DecisionPolicy?> FindPolicyAsync(CancellationToken cancellationToken);

    Task AddPolicyAsync(DecisionPolicy policy, CancellationToken cancellationToken);
}
