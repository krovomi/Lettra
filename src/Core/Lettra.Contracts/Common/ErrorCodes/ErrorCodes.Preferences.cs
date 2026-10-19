namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : préférences de l'utilisateur, rangées dans son compte.</summary>
public static partial class ErrorCodes
{
    public static class Preferences
    {
        /// <summary>Une valeur ne respecte pas son format ou sa longueur.</summary>
        public const string Invalid = "Preferences.Invalid";

        /// <summary>Trop d'éléments dans une liste (favoris, dossiers, valeurs de saisie).</summary>
        public const string TooMany = "Preferences.TooMany";

        /// <summary>Installation sans base Catalog (poste isolé) : les préférences restent sur l'appareil.</summary>
        public const string Unavailable = "Preferences.Unavailable";
    }
}
