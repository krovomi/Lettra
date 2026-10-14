using Lettra.Domain.Common;

namespace Lettra.Supervision.Domain;

/// <summary>Codes d'erreur du module Supervision. Jamais de texte : l'interface les traduit.</summary>
public static class SupervisionErrors
{
    public const string ScopeIncompleteCode = "Alert.ScopeIncomplete";
    public const string RuleRequiredCode = "Alert.RuleRequired";
    public const string ReasonRequiredCode = "Alert.ReasonRequired";
    public const string AgentNotAllowedCode = "Alert.AgentNotAllowed";
    public const string HumanRequiredCode = "Alert.HumanRequired";
    public const string InvalidTransitionCode = "Alert.InvalidTransition";
    public const string SameSeverityCode = "Alert.SameSeverity";
    public const string NotFoundCode = "Alert.NotFound";

    public static Error ScopeIncomplete => Error.Validation(ScopeIncompleteCode);

    public static Error RuleRequired => Error.Validation(RuleRequiredCode);

    public static Error ReasonRequired => Error.Validation(ReasonRequiredCode, "Reason");

    /// <summary>Vigie ne ferme, n'acquitte, ne met en sourdine ni ne change la gravité (ADR-0014).</summary>
    public static Error AgentNotAllowed => new(AgentNotAllowedCode, ErrorKind.Forbidden);

    /// <summary>Changer la gravité est un geste humain, motivé et tracé.</summary>
    public static Error HumanRequired => new(HumanRequiredCode, ErrorKind.Forbidden);

    public static Error InvalidTransition => Error.Conflict(InvalidTransitionCode);

    public static Error SameSeverity => Error.Validation(SameSeverityCode, "Severity");

    public static Error NotFound => Error.NotFound(NotFoundCode);
}
