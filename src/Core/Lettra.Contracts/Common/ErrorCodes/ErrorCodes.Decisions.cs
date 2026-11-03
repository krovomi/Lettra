namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : décisions à prendre.</summary>
public static partial class ErrorCodes
{
    public static class Decision
    {
        public const string NotFound = "Decision.NotFound";
        public const string NotOpen = "Decision.NotOpen";
        public const string NotRestorable = "Decision.NotRestorable";
        public const string SnoozeDateInvalid = "Decision.SnoozeDateInvalid";
        public const string NoAction = "Decision.NoAction";
        public const string ActionUnavailable = "Decision.ActionUnavailable";
        public const string LevelInvalid = "Decision.LevelInvalid";
        public const string ChoiceInvalid = "Decision.ChoiceInvalid";
    }
}
