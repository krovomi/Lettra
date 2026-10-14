namespace Lettra.Supervision.Domain;

/// <summary>Qui agit sur une alerte. Tracé dans la chronologie, comme <c>AssistantIA</c> dans l'audit (annexe B).</summary>
public enum ActorKind
{
    /// <summary>Une personne, avec ses droits.</summary>
    Human = 1,

    /// <summary>Une règle déterministe (déclenchement, escalade, résolution automatique).</summary>
    Rule = 2,

    /// <summary>L'agent de tri Vigie : ne peut que proposer (annexe G §7.2).</summary>
    AgentVigie = 3,
}

/// <summary>Auteur d'une action. <paramref name="Id"/> est un identifiant technique (utilisateur, règle), jamais un nom.</summary>
public sealed record Actor(ActorKind Kind, string Id)
{
    public static Actor Human(Guid userId) => new(ActorKind.Human, userId.ToString());

    public static Actor Rule(string ruleId) => new(ActorKind.Rule, ruleId);

    public static Actor Vigie { get; } = new(ActorKind.AgentVigie, "vigie");
}
