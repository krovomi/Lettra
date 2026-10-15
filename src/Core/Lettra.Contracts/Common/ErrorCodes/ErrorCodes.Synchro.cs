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
    }
}
