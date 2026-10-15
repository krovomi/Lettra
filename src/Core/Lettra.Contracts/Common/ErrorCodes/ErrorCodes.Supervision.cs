namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : centre d'alertes (module Supervision, annexe G).</summary>
public static partial class ErrorCodes
{
    public static class Alerts
    {
        public const string ScopeIncomplete = "Alert.ScopeIncomplete";
        public const string RuleRequired = "Alert.RuleRequired";
        public const string ReasonRequired = "Alert.ReasonRequired";
        public const string AgentNotAllowed = "Alert.AgentNotAllowed";
        public const string HumanRequired = "Alert.HumanRequired";
        public const string InvalidTransition = "Alert.InvalidTransition";
        public const string SameSeverity = "Alert.SameSeverity";
        public const string NotFound = "Alert.NotFound";
    }
}
