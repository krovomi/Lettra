namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : assistant IA (annexe B).</summary>
public static partial class ErrorCodes
{
    public static class Ai
    {
        public const string Disabled = "Ai.Disabled";
        public const string FeatureDisabled = "Ai.FeatureDisabled";
        public const string FeatureBlocked = "Ai.FeatureBlocked";
        public const string DossierExcluded = "Ai.DossierExcluded";
        public const string EngineUnavailable = "Ai.EngineUnavailable";
        public const string Refused = "Ai.Refused";
        public const string ToolRoundsExceeded = "Ai.ToolRoundsExceeded";
        public const string Truncated = "Ai.Truncated";
        public const string ToolRejected = "Ai.ToolRejected";
        public const string UngroundedAmounts = "Ai.UngroundedAmounts";
        public const string UnknownReferences = "Ai.UnknownReferences";
        public const string SkillPackStale = "Ai.SkillPackStale";
        public const string SettingsUnavailable = "Ai.SettingsUnavailable";
        public const string EngineRequired = "Ai.EngineRequired";
        public const string SettingsConflict = "Ai.SettingsConflict";
    }
}
