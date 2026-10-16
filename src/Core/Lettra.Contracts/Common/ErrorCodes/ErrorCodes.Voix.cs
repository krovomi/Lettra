namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : dictée et commandes vocales (vague 5, lane D).</summary>
public static partial class ErrorCodes
{
    public static class Speech
    {
        /// <summary>Aucun moteur vocal n'est configuré sur ce déploiement (réglage par défaut).</summary>
        public const string Disabled = "Speech.Disabled";

        /// <summary>Le moteur configuré ne répond pas (serveur Whisper de la cellule arrêté, modèle illisible).</summary>
        public const string EngineUnavailable = "Speech.EngineUnavailable";

        /// <summary>Le modèle Whisper du poste n'est pas encore téléchargé et le réseau manque, ou son empreinte est fausse.</summary>
        public const string ModelUnavailable = "Speech.ModelUnavailable";

        /// <summary>L'audio n'est pas un WAV PCM 16 bits, mono, 16 kHz.</summary>
        public const string AudioInvalid = "Speech.AudioInvalid";

        /// <summary>L'enregistrement dépasse la durée maximale du déploiement.</summary>
        public const string AudioTooLong = "Speech.AudioTooLong";

        /// <summary>Langue non proposée par ce déploiement.</summary>
        public const string LanguageUnsupported = "Speech.LanguageUnsupported";

        /// <summary>Le moteur n'a reconnu aucune parole (silence, bruit).</summary>
        public const string NothingHeard = "Speech.NothingHeard";
    }
}
