namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : module Synchro (phase 4, ADR-0008).</summary>
public static partial class ErrorCodes
{
    public static class Sync
    {
        /// <summary>La même clé d'idempotence revient avec un contenu différent : l'intention est refusée.</summary>
        public const string IdempotencyKeyReused = "Sync.IdempotencyKeyReused";

        /// <summary>Le jeton de synchronisation n'est pas un jeton émis par ce serveur.</summary>
        public const string TokenInvalid = "Sync.TokenInvalid";

        /// <summary>Lot vide ou trop gros (au plus <c>SyncHttp.MaxBatch</c> intentions).</summary>
        public const string BatchInvalid = "Sync.BatchInvalid";

        /// <summary>L'élément visé par une modification n'existe pas sur le serveur.</summary>
        public const string EntityNotFound = "Sync.EntityNotFound";

        /// <summary>La série n'est pas une série ouverte de ce poste (ADR-0007) : la pièce n'est pas reçue.</summary>
        public const string DeviceSeriesUnknown = "Sync.DeviceSeriesUnknown";

        /// <summary>Le numéro n'est pas le suivant de la série du poste : une pièce précédente manque ou a été refusée.</summary>
        public const string NumberOutOfOrder = "Sync.NumberOutOfOrder";

        /// <summary>La version du cabinet n'est pas encore arrivée sur le poste : synchroniser avant de garder sa saisie.</summary>
        public const string ConflictAwaitingRefresh = "Sync.ConflictAwaitingRefresh";

        /// <summary>Le cabinet a révoqué ce poste : il est refusé, et sa réplique est effacée (étape 4.4).</summary>
        public const string DeviceRevoked = "Sync.DeviceRevoked";

        /// <summary>Le poste est resté hors ligne plus longtemps que le cabinet ne le permet : il ne valide plus.</summary>
        public const string OfflineTooLong = "Sync.OfflineTooLong";

        /// <summary>Durée maximale hors ligne hors des bornes (de 1 heure à 30 jours).</summary>
        public const string PolicyInvalid = "Sync.PolicyInvalid";

        /// <summary>Ce poste n'est pas connu du dossier.</summary>
        public const string DeviceUnknown = "Sync.DeviceUnknown";
    }
}
